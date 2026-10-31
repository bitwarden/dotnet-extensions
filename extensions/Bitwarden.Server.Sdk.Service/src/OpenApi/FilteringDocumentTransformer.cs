using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Publishes an operation's filters under the names a caller sends them.
/// </summary>
/// <remarks>
/// <para>
/// A filter is declared as a <c>Filter&lt;T&gt;</c> parameter, so the API explorer already knows
/// its name, whether it is optional, and the documentation the author wrote for it. Only two
/// things are wrong: it is published under the bare parameter name rather than
/// <c>filter[name]</c>, and its schema is the platform's wrapper type rather than what the
/// caller sends.
/// </para>
/// <para>
/// The author's <c>&lt;param&gt;</c> description is carried across rather than discarded.
/// </para>
/// <para>
/// A document transformer, and that is not incidental. XML comment support is registered by the
/// service's own <c>AddOpenApi()</c> call, which runs after the platform's, and it matches
/// documentation to parameters by name. Renaming <c>id</c> to <c>filter[id]</c> any earlier
/// leaves it nothing to match, and every filter loses its description.
/// </para>
/// </remarks>
internal sealed class FilteringDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            if (!document.Paths.TryGetValue($"/{description.RelativePath?.TrimEnd('/')}", out var path)
                || path.Operations is null
                || description.HttpMethod is null
                || !path.Operations.TryGetValue(HttpMethod.Parse(description.HttpMethod), out var operation))
            {
                continue;
            }

            Rename(operation, description);
        }

        return Task.CompletedTask;
    }

    private static void Rename(
        OpenApiOperation operation,
        Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription description)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        var filters = description.ActionDescriptor.Parameters
            .Select(parameter => (parameter.Name, Shape: FilterShape.Of(parameter.ParameterType)))
            .Where(candidate => candidate.Shape is not null)
            .ToDictionary(candidate => candidate.Name, candidate => candidate.Shape!.Value, StringComparer.Ordinal);

        if (filters.Count == 0)
        {
            return;
        }

        // The collection is read-only in place, so each filter is replaced by a corrected copy
        // that carries the author's description across.
        for (var i = 0; i < operation.Parameters.Count; i++)
        {
            if (operation.Parameters[i].Name is not { } name || !filters.TryGetValue(name, out var shape))
            {
                continue;
            }

            var declared = operation.Parameters[i];

            if (shape.Range)
            {
                operation.Parameters[i] = Range(name, declared, Element(shape.Element));
                continue;
            }

            var element = Element(shape.Element);
            var text = declared.Description;

            if (shape.Many)
            {
                text = string.IsNullOrEmpty(text)
                    ? "May be repeated, comma-delimited, or both."
                    : $"{text} May be repeated, comma-delimited, or both.";
            }

            // A list filter is left at the query default of style "form", explode true: the
            // repeated form, which is what a generator emits and what the binder reads. Saying so
            // explicitly changes nothing, because Microsoft.OpenApi omits a stated default. The
            // binder also accepts commas, which OpenAPI cannot express alongside it, so the
            // description says so instead.
            operation.Parameters[i] = new OpenApiParameter
            {
                Name = $"filter[{name}]",
                In = ParameterLocation.Query,
                Required = declared.Required,
                Description = text,
                Schema = shape.Many
                    ? new OpenApiSchema { Type = JsonSchemaType.Array, Items = element }
                    : element,
            };
        }
    }

    /// <remarks>
    /// A string, because that is what travels: OpenAPI has no notation for an interval, so the
    /// syntax is described in words and constrained by a pattern a generator or a validator can check
    /// without knowing what the bounds mean. The bounds' own type is named in the text.
    /// </remarks>
    private static OpenApiParameter Range(string name, IOpenApiParameter declared, OpenApiSchema bound)
    {
        var kind = bound.Format is { } format ? format : bound.Type.ToString()!.ToLowerInvariant();
        var syntax = "A range: [ or ( to include or exclude the lower bound, then the two bounds "
            + $"separated by a comma, then ] or ) for the upper. Each bound is a {kind}, or * for no "
            + "limit, so [2000-01-01T00:00:00Z,2001-01-01T00:00:00Z) is the year 2000.";

        return new OpenApiParameter
        {
            Name = $"filter[{name}]",
            In = ParameterLocation.Query,
            Required = declared.Required,
            Description = string.IsNullOrEmpty(declared.Description) ? syntax : $"{declared.Description} {syntax}",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = @"^[\[(].+,.+[\])]$",
            },
        };
    }

    private static OpenApiSchema Element(Type type)
    {
        if (type == typeof(DateTimeOffset) || type == typeof(DateTime))
        {
            return new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" };
        }

        if (type.IsEnum)
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. EnumValues.Of(type).Select(value => (JsonNode)JsonValue.Create(value))],
            };
        }

        if (type == typeof(Guid))
        {
            return new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" };
        }

        if (type == typeof(bool))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Boolean };
        }

        if (type == typeof(int) || type == typeof(long))
        {
            return new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = type == typeof(int) ? "int32" : "int64",
            };
        }

        return new OpenApiSchema { Type = JsonSchemaType.String };
    }
}
