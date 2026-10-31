using System.ComponentModel;
using System.Globalization;
using System.IO.Pipelines;
using System.Reflection;
using System.Security.Claims;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Classifies a handler's parameters, and refuses the ones the standards cannot check.
/// </summary>
internal static class RequestParameters
{
    /// <summary>
    /// Every parameter of the handler, or the problems that stop it being served.
    /// </summary>
    /// <param name="method">The handler.</param>
    /// <param name="services">The application's services, to tell a dependency from a value.</param>
    /// <param name="problems">Where a parameter the standards cannot check is reported.</param>
    public static IReadOnlyList<RequestParameter> For(MethodInfo method, IServiceProvider services, ICollection<string> problems)
    {
        var isService = services.GetService<IServiceProviderIsService>();
        var parameters = method.GetParameters();
        var classified = new List<RequestParameter>(parameters.Length);

        foreach (var parameter in parameters)
        {
            if (Classify(parameter, isService) is { } each)
            {
                classified.Add(each);
            }
            else
            {
                problems.Add(
                    $"'{StandardEndpointConvention.NameOf(method)}' takes '{parameter.Name}' as a "
                    + $"{parameter.ParameterType.Name}, which the platform cannot bind and validate. A handler takes "
                    + "route, query and header values of simple types, a resource document, a JSON body, the "
                    + "platform's query types, and its dependencies.");
            }
        }

        return classified;
    }

    private static RequestParameter? Classify(ParameterInfo parameter, IServiceProviderIsService? isService)
    {
        var index = parameter.Position;
        var type = parameter.ParameterType;
        var name = parameter.Name ?? string.Empty;
        var attributes = parameter.GetCustomAttributes(inherit: true);

        RequestParameter Make(ParameterKind kind, string modelName, ValuePlace place = ValuePlace.RouteOrQuery, bool required = false) =>
            new(index, parameter, kind, modelName, place, required);

        if (IsFramework(type)
            || attributes.OfType<IFromServiceMetadata>().Any()
            || attributes.OfType<FromKeyedServicesAttribute>().Any())
        {
            return Make(ParameterKind.Ignored, name);
        }

        if (attributes.OfType<AsParametersAttribute>().Any()
            || (attributes.OfType<IFromFormMetadata>().Any() && !IsSimple(type) && type != typeof(IFormFile)
                && type != typeof(IFormFileCollection) && type != typeof(IFormCollection)))
        {
            return Make(ParameterKind.Model, name, required: !IsNullable(parameter));
        }

        if (attributes.OfType<IFromFormMetadata>().Any())
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ResourceDocument<>))
        {
            return Make(ParameterKind.Document, name, required: !IsNullable(parameter));
        }

        if (BindsItself(type))
        {
            return Make(ParameterKind.Bound, name, required: !IsNullable(parameter) && !parameter.HasDefaultValue);
        }

        var header = attributes.OfType<IFromHeaderMetadata>().FirstOrDefault();
        var query = attributes.OfType<IFromQueryMetadata>().FirstOrDefault();
        var route = attributes.OfType<IFromRouteMetadata>().FirstOrDefault();

        if (IsSimple(type) || (type.IsArray && IsSimple(type.GetElementType()!)))
        {
            var place = header is not null ? ValuePlace.Header
                : query is not null ? ValuePlace.Query
                : route is not null ? ValuePlace.Route
                : ValuePlace.RouteOrQuery;
            var modelName = header?.Name ?? query?.Name ?? route?.Name ?? name;

            return Make(ParameterKind.Simple, modelName, place, required: !IsNullable(parameter) && !parameter.HasDefaultValue && !type.IsArray);
        }

        if (header is not null || query is not null || route is not null)
        {
            return null;
        }

        if (isService?.IsService(type) == true)
        {
            return Make(ParameterKind.Ignored, name);
        }

        return Make(ParameterKind.Body, name, required: !IsNullable(parameter));
    }

    /// <summary>
    /// A value the framework supplies from the request itself rather than from what the caller sent.
    /// </summary>
    private static bool IsFramework(Type type) =>
        type == typeof(CancellationToken)
        || type == typeof(HttpContext)
        || type == typeof(HttpRequest)
        || type == typeof(HttpResponse)
        || type == typeof(ClaimsPrincipal)
        || type == typeof(Stream)
        || type == typeof(PipeReader);

    /// <summary>
    /// The platform's query types: <c>filter[…]</c>, <c>sort</c>, <c>fields[…]</c> and the page parameters.
    /// </summary>
    internal static bool IsQueryType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying == typeof(Querying.Sort)
            || underlying == typeof(Querying.PageNumber)
            || underlying == typeof(Querying.PageSize)
            || underlying == typeof(Querying.PageAfter)
            || (underlying.IsGenericType
                && (underlying.GetGenericTypeDefinition() == typeof(Querying.Filter<>)
                    || underlying.GetGenericTypeDefinition() == typeof(Querying.Fields<>)));
    }

    /// <summary>
    /// Binds one of the platform's query types as its <c>BindAsync</c> would, throwing what it would
    /// throw, for a refusal that must know what binding would have said.
    /// </summary>
    internal static object? BindQueryType(ParameterInfo parameter, HttpRequest request)
    {
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        if (type == typeof(Querying.Sort))
        {
            return Querying.SortBinding.Bind(
                request.Query,
                parameter.Member is MethodInfo method ? Querying.SortableFields.ResourceOf(method.ReturnType) : null);
        }

        if (type == typeof(Querying.PageNumber) || type == typeof(Querying.PageSize) || type == typeof(Querying.PageAfter))
        {
            return Querying.PageBinding.Bind(
                type,
                request.Query,
                parameter.GetCustomAttributes<DefaultValueAttribute>().Select(attribute => attribute.Value).OfType<int>().Cast<int?>().FirstOrDefault());
        }

        return type.GetGenericTypeDefinition() == typeof(Querying.Filter<>)
            ? Querying.FilterBinding.Bind(type, parameter.Name ?? string.Empty, request.Query)
            : Querying.FieldsBinding.Bind(type, request.Query);
    }

    /// <summary>
    /// Whether the type has the framework's <c>BindAsync</c>, so binds itself.
    /// </summary>
    /// <remarks>
    /// A <see cref="Nullable{T}"/> binds through <c>T</c>'s, as the framework binds it.
    /// </remarks>
    internal static bool BindsItself(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.GetMethod("BindAsync", BindingFlags.Public | BindingFlags.Static, [typeof(HttpContext), typeof(ParameterInfo)]) is not null
            || underlying.GetMethod("BindAsync", BindingFlags.Public | BindingFlags.Static, [typeof(HttpContext)]) is not null;
    }

    /// <summary>
    /// The types a route, query or header value may be: what an MVC controller's binder converts from a string.
    /// </summary>
    internal static bool IsSimple(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(Guid)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan);
    }

    /// <summary>
    /// Whether a request may leave the parameter out: nullable, or a value with a default.
    /// </summary>
    private static bool IsNullable(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;

        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) is not null;
        }

        // A context of its own: one is not safe to share between endpoints built at once.
        return new NullabilityInfoContext().Create(parameter).ReadState != NullabilityState.NotNull;
    }

    /// <summary>
    /// Converts a value as an MVC controller's binder would, to say whether it is one: the type's
    /// converter, in the invariant culture.
    /// </summary>
    public static bool Converts(Type type, string value)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return true;
        }

        try
        {
            return TypeDescriptor.GetConverter(underlying).ConvertFromString(null, CultureInfo.InvariantCulture, value) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
