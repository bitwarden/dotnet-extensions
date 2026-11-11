using Bitwarden.Server.Sdk.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
    /// Names what is wrong with a route, query or header value, as a controller's binder would.
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

            return;
        }

        var element = parameter.Info.ParameterType.IsArray ? parameter.Info.ParameterType.GetElementType()! : parameter.Underlying;

        foreach (var value in parameter.Info.ParameterType.IsArray ? values : [values[^1]])
        {
            if (string.IsNullOrEmpty(value))
            {
                if (element.IsValueType && Nullable.GetUnderlyingType(parameter.Info.ParameterType) is null && !parameter.Info.ParameterType.IsArray)
                {
                    errors.Add(new ModelError(parameter.ModelName, $"The value '{value}' is invalid."));
                    sources[parameter.ModelName] = source;
                }

                continue;
            }

            if (!RequestParameters.Converts(element, value))
            {
                errors.Add(new ModelError(parameter.ModelName, $"The value '{value}' is not valid."));
                sources[parameter.ModelName] = source;
                return;
            }
        }
    }

    /// <summary>
    /// A simple value's raw text, and whether it came from the query.
    /// </summary>
    private static (IReadOnlyList<string?> Values, bool FromQuery) Read(HttpContext http, RequestParameter parameter)
    {
        switch (parameter.Place)
        {
            case ValuePlace.Header:
                return ([.. http.Request.Headers[parameter.ModelName]], false);

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
        List<ModelError> errors,
        Dictionary<string, ErrorSource> sources)
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
