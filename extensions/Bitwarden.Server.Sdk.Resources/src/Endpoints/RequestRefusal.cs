using System.ComponentModel.DataAnnotations;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Resources.Endpoints;

/// <summary>
/// How a request that failed binding or validation is refused: what an MVC controller's
/// <c>[ApiController]</c> answers from its model state, in its words and its order.
/// </summary>
internal static class RequestRefusal
{
    /// <summary>
    /// The 422 for every failure found, ordered as a controller's model state lists them.
    /// </summary>
    public static IResult Unprocessable(IReadOnlyList<ModelError> errors, IReadOnlyDictionary<string, ErrorSource> sources) =>
        Errors(StatusCodes.Status422UnprocessableEntity, [.. ModelValidation.InModelStateOrder(errors)
            .Select(error => new Error
            {
                Status = StatusCodes.Status422UnprocessableEntity.ToString(),
                Code = ErrorCodes.InvalidValue,
                Title = ErrorTitles.InvalidValue,
                Detail = error.Message,
                Source = sources.TryGetValue(error.Key, out var source)
                    ? source
                    : new ErrorSource { Pointer = RequestPointer.For(error.Key) },
            })]);

    /// <summary>
    /// Names what is wrong with a route, query or header value, as a controller's binder would, then
    /// holds a value that binds to the parameter's validation attributes, as a controller's
    /// validation would.
    /// </summary>
    public static void CheckSimple(
        HttpContext http,
        RequestParameter parameter,
        List<ModelError> errors,
        Dictionary<string, ErrorSource> sources)
    {
        var (values, fromQuery) = Read(http, parameter);
        var source = fromQuery
            ? new ErrorSource { Parameter = parameter.ModelName }
            : new ErrorSource { Pointer = RequestPointer.For(parameter.ModelName) };

        if (values.Count == 0)
        {
            if (parameter.Required)
            {
                errors.Add(new ModelError(parameter.ModelName, Required(parameter.DisplayName)));
                sources[parameter.ModelName] = source;
            }

            // A controller validates a parameter nothing was sent for only when it is required: declared so, or a
            // value that cannot be null. It validates it as null, which only a rule requiring a value refuses.
            else if (parameter.Validators.Count > 0
                && (parameter.Validators.Any(validator => validator is RequiredAttribute)
                    || Nullable.GetUnderlyingType(parameter.Info.ParameterType) is null && parameter.Info.ParameterType.IsValueType))
            {
                Validate(http, parameter, value: null, errors, sources, source);
            }

            return;
        }

        var isArray = parameter.Info.ParameterType.IsArray;
        var declared = isArray ? parameter.Info.ParameterType.GetElementType()! : parameter.Info.ParameterType;
        var element = Nullable.GetUnderlyingType(declared) ?? declared;
        var bound = new List<object?>();
        var refused = false;

        // Whitespace is a value to convert for a nullable value type, which a controller would bind as none but the
        // platform cannot bind at all: refused by name rather than with nothing said.
        var nullableValue = Nullable.GetUnderlyingType(declared) is not null;

        // A single value is the first sent, as a controller's binder takes it, and named in a refusal as every value
        // sent, joined.
        foreach (var value in isArray ? values : [values[0]])
        {
            var named = isArray || values.Count == 1 ? value : new StringValues([.. values]).ToString();

            // A controller's binder reads an empty or blank value as none, which a value that cannot be null refuses.
            if (nullableValue ? string.IsNullOrEmpty(value) : string.IsNullOrWhiteSpace(value))
            {
                if (declared.IsValueType && !nullableValue)
                {
                    errors.Add(new ModelError(parameter.ModelName, $"The value '{named}' is invalid."));
                    refused = true;
                    continue;
                }

                // A required value sent blank is refused as one not sent is: the handler would receive it as none.
                if (parameter.Required)
                {
                    errors.Add(new ModelError(parameter.ModelName, Required(parameter.DisplayName)));
                    sources[parameter.ModelName] = source;
                    return;
                }

                bound.Add(null);
                continue;
            }

            if (!RequestParameters.Converts(element, value))
            {
                errors.Add(new ModelError(parameter.ModelName, $"The value '{named}' is not valid."));
                refused = true;
                continue;
            }

            bound.Add(Convert(element, value));
        }

        // Every value of an array that fails is named, in the order sent, as a controller's binder names them.
        if (refused)
        {
            sources[parameter.ModelName] = source;
            return;
        }

        if (parameter.Validators.Count == 0)
        {
            return;
        }

        if (isArray)
        {
            var array = Array.CreateInstance(declared, bound.Count);

            for (var i = 0; i < bound.Count; i++)
            {
                array.SetValue(bound[i], i);
            }

            Validate(http, parameter, array, errors, sources, source);
        }
        else
        {
            Validate(http, parameter, bound[0], errors, sources, source);
        }
    }

    /// <summary>
    /// Holds a simple value to its parameter's validation attributes, a failure keyed and pointed as a
    /// binding failure of the same value is.
    /// </summary>
    private static void Validate(
        HttpContext http,
        RequestParameter parameter,
        object? value,
        List<ModelError> errors,
        Dictionary<string, ErrorSource> sources,
        ErrorSource source)
    {
        var before = errors.Count;

        ModelValidation.ValidateParameter(parameter, value, parameter.ModelName, http.RequestServices, errors);

        if (errors.Count > before)
        {
            sources[parameter.ModelName] = source;
        }
    }

    /// <summary>
    /// A value that is not blank converted as a controller's binder converts it: a string as it came, anything else by
    /// its type's converter, in the invariant culture.
    /// </summary>
    private static object? Convert(Type type, string value) =>
        type == typeof(string)
            ? value
            : System.ComponentModel.TypeDescriptor.GetConverter(type).ConvertFromString(null, System.Globalization.CultureInfo.InvariantCulture, value);

    /// <summary>
    /// A simple value's raw text, as a controller's binder reads it, and whether it came from the query.
    /// </summary>
    internal static (IReadOnlyList<string?> Values, bool FromQuery) Read(HttpContext http, RequestParameter parameter)
    {
        switch (parameter.Place)
        {
            // An array's values are the header's, split at commas, as a controller's binder and the platform split them;
            // a single value is every one the header was sent with, joined.
            case ValuePlace.Header:
                if (parameter.Info.ParameterType.IsArray)
                {
                    return (http.Request.Headers.GetCommaSeparatedValues(parameter.ModelName), false);
                }

                var header = http.Request.Headers[parameter.ModelName];
                if (header.Count == 0)
                {
                    return ([], false);
                }

                return ([header.ToString()], false);

            case ValuePlace.Query:
                return ([.. http.Request.Query[parameter.ModelName]], true);

            default:
                if (http.Request.RouteValues.TryGetValue(parameter.ModelName, out var routed))
                {
                    return ([routed?.ToString()], false);
                }

                return parameter.Place == ValuePlace.Route
                    ? ([], false)
                    : ([.. http.Request.Query[parameter.ModelName]], true);
        }
    }

    /// <summary>
    /// Names what is wrong with the members of a model bound from the query, as a controller's binder
    /// would: each member that does not parse, by the name it was sent under.
    /// </summary>
    public static void CheckMembers(
        HttpContext http,
        RequestParameter parameter,
        List<ModelError> errors)
    {
        foreach (var property in parameter.Underlying.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!RequestParameters.IsSimple(property.PropertyType) || property.SetMethod is null)
            {
                continue;
            }

            var name = ModelValidation.BoundName(property);
            var raw = parameter.Info.IsDefined(typeof(Microsoft.AspNetCore.Mvc.FromFormAttribute), inherit: true)
                ? (http.Request.HasFormContentType ? http.Request.Form[name].FirstOrDefault() : null)
                : http.Request.RouteValues.TryGetValue(name, out var routed)
                ? routed?.ToString()
                : property.IsDefined(typeof(Microsoft.AspNetCore.Mvc.FromHeaderAttribute), inherit: true)
                    ? http.Request.Headers[name].FirstOrDefault()
                    : http.Request.Query[name].FirstOrDefault();

            if (!string.IsNullOrEmpty(raw) && !RequestParameters.Converts(property.PropertyType, raw))
            {
                // A member's message names it, where a parameter's does not.
                errors.Add(new ModelError(name, $"The value '{raw}' is not valid for {property.Name}."));
            }
        }
    }

    /// <summary>
    /// Fills a form model's members from the fields their <c>[FromForm(Name = …)]</c> names, as a
    /// controller binds them: the framework binds a form model's members by their own names only.
    /// </summary>
    public static void FillFromForm(HttpContext http, RequestParameter parameter, object model)
    {
        if (!parameter.Info.IsDefined(typeof(Microsoft.AspNetCore.Mvc.FromFormAttribute), inherit: true)
            || !http.Request.HasFormContentType)
        {
            return;
        }

        foreach (var property in model.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (property.SetMethod is null || !RequestParameters.IsSimple(property.PropertyType))
            {
                continue;
            }

            var values = http.Request.Form[ModelValidation.BoundName(property)];

            if (values.Count == 0)
            {
                continue;
            }

            // The first value, and an empty or blank one as none, as a controller's binder reads a field.
            if (values[0] is not { } raw || string.IsNullOrWhiteSpace(raw))
            {
                if (!property.PropertyType.IsValueType || Nullable.GetUnderlyingType(property.PropertyType) is not null)
                {
                    property.SetValue(model, null);
                }

                continue;
            }

            var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (target == typeof(string))
            {
                property.SetValue(model, raw);
            }
            else if (RequestParameters.Converts(target, raw))
            {
                property.SetValue(model, System.ComponentModel.TypeDescriptor.GetConverter(target)
                    .ConvertFromString(null, System.Globalization.CultureInfo.InvariantCulture, raw));
            }
        }
    }

    public static string Required(string name) => $"The {name} field is required.";

    public static IResult Errors(int status, IReadOnlyList<Error> errors) =>
        new ErrorResult(status, new ErrorResponse { Errors = errors });

    /// <summary>
    /// The standard error document, written with the platform's JSON options.
    /// </summary>
    private sealed class ErrorResult(int status, ErrorResponse response) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = status;

            return httpContext.Response.WriteAsJsonAsync(response, httpContext.RequestServices
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
                .Value.SerializerOptions);
        }
    }

}
