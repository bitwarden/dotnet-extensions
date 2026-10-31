using System.Reflection;
using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Endpoints;

/// <summary>
/// Refuses to start a service whose endpoints break a rule the whole surface must keep,
/// naming every one.
/// </summary>
/// <remarks>
/// <para>
/// What is checked, over every endpoint the service maps:
/// </para>
/// <list type="bullet">
///   <item><description>What the conventions and the standard filter found while building the endpoints.</description></item>
///   <item><description>
///     An operation identifier is unique across the service, since every endpoint publishes into
///     one OpenAPI document.
///   </description></item>
///   <item><description>
///     A standard endpoint is mapped with its attribute's verb, at its attribute's route, so the
///     <c>Map</c> line and the attribute cannot drift apart.
///   </description></item>
///   <item><description>A standard endpoint is in the OpenAPI document: every API a service exposes is published.</description></item>
///   <item><description>
///     A filter expression reaches a handler only where the platform checks it against the
///     resource's filterable fields: a search or bulk request on its standard operation.
///   </description></item>
/// </list>
/// <para>
/// Reading the endpoints builds them, which runs every convention and filter factory, so this is
/// also the moment those report.
/// </para>
/// </remarks>
internal static class StandardEndpointCheck
{
    private static readonly Regex Constraint = new(@"\{(\*{0,2})([^}:=?]+)[^}]*\}", RegexOptions.Compiled);

    public static void Require(WebApplication app)
    {
        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var problems = new List<string>(app.Services.GetRequiredService<StandardEndpointProblems>().All);
        var named = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var endpoint in endpoints)
        {
            var metadata = endpoint.Metadata;
            var route = endpoint.RoutePattern.RawText ?? string.Empty;
            var method = metadata.GetMetadata<MethodInfo>();
            var here = method is null ? route : StandardEndpointConvention.NameOf(method);

            if (metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is { } name
                && metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription != true)
            {
                if (named.TryGetValue(name, out var taken) && taken != here)
                {
                    problems.Add(
                        $"'{taken}' and '{here}' would both be published as '{name}', and an operation identifier "
                        + "must be unique across the service.");
                }
                else
                {
                    named[name] = here;
                }
            }

            if (method is not null)
            {
                Unchecked(method, standardized: metadata.GetMetadata<StandardEndpointMetadata>() is not null, problems);
            }

            if (metadata.GetMetadata<StandardEndpointMetadata>() is null || method is null)
            {
                continue;
            }

            if (metadata.GetMetadata<IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription == true)
            {
                problems.Add($"'{here}' answers requests but is not in the OpenAPI document, and every API a service exposes is published.");
            }

            if (method.GetCustomAttributes(inherit: true).OfType<IStandardOperation>().FirstOrDefault() is HttpMethodAttribute standard)
            {
                var verbs = metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

                if (!verbs.OrderBy(verb => verb).SequenceEqual(standard.HttpMethods.OrderBy(verb => verb), StringComparer.OrdinalIgnoreCase))
                {
                    problems.Add(
                        $"'{here}' is [{AttributeName(standard)}], which is {string.Join(", ", standard.HttpMethods)}, "
                        + $"but is mapped with {(verbs.Count == 0 ? "every verb" : string.Join(", ", verbs))}.");
                }

                if (!Matches(route, standard.Template))
                {
                    problems.Add(
                        $"'{here}' is [{AttributeName(standard)}] at '{standard.Template}', but is mapped at '/{route.Trim('/')}'.");
                }
            }
        }

        foreach (var check in app.Services.GetServices<IStandardEndpointCheck>())
        {
            check.Check(endpoints, problems);
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The service's endpoints do not meet the standards:" + string.Concat(problems.Select(problem => $"{System.Environment.NewLine}  - {problem}")));
        }
    }

    /// <summary>
    /// Refuses a handler taking a filter expression the platform will not check against the
    /// resource's filterable fields before it runs.
    /// </summary>
    /// <remarks>
    /// The standard filter checks the expression in a <see cref="Search{T}"/> or bulk request, and
    /// only on an operation marked as the search or bulk operation it is. Anywhere else it would
    /// reach the handler unchecked, free to name any property of the entity.
    /// </remarks>
    internal static void Unchecked(MethodInfo method, bool standardized, List<string> problems)
    {
        var checks = standardized
            && BulkChecks.ChecksFilter(method.GetCustomAttributes(inherit: true).OfType<IStandardOperation>().FirstOrDefault());

        foreach (var parameter in method.GetParameters())
        {
            var type = parameter.ParameterType;
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
            var filtered = definition == typeof(Search<>) || definition == typeof(BulkUpdate<>)
                || definition == typeof(BulkDelete<>) || definition == typeof(BulkAction<>)
                || definition == typeof(BulkAction<,>);

            var uncheckedFilter = filtered && !checks;

            if (type == typeof(Query) || uncheckedFilter)
            {
                problems.Add(
                    $"'{StandardEndpointConvention.NameOf(method)}' takes '{parameter.Name}' as a "
                    + $"{(definition ?? type).Name.Split('`')[0]}, whose filter would reach it without being checked against "
                    + "the fields the resource marks filterable. Take a Search<T> on a [StandardSearch] operation, or a "
                    + "bulk request on the [StandardBulk...] operation it belongs to, in a group mapped WithStandards().");
            }
        }
    }

    /// <summary>
    /// Whether an endpoint's full route is where its attribute puts it: the attribute's own route
    /// when it is absolute, and otherwise the attribute's route beneath the group's.
    /// </summary>
    /// <remarks>
    /// Route constraints are the mapping's to add, so they are not compared.
    /// </remarks>
    internal static bool Matches(string route, string? template)
    {
        var mapped = Normalize(route);
        var declared = template ?? string.Empty;

        if (declared.StartsWith("~/", StringComparison.Ordinal) || declared.StartsWith('/'))
        {
            return mapped == Normalize(declared.TrimStart('~'));
        }

        declared = Normalize(declared);

        return declared.Length == 0
            || mapped == declared
            || mapped.EndsWith('/' + declared, StringComparison.Ordinal);
    }

    private static string Normalize(string route) =>
        Constraint.Replace(route.Trim().Trim('/'), match => $"{{{match.Groups[1].Value}{match.Groups[2].Value}}}");

    private static string AttributeName(object attribute) =>
        attribute.GetType().Name is var name && name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : attribute.GetType().Name;
}
