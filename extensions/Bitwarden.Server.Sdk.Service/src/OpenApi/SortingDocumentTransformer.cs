using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Publishes the <c>sort</c> parameter, the fields it accepts, and the order used without it.
/// </summary>
/// <remarks>
/// <para>
/// A caller sends a sort as a string, and the fields it may name are read from the resource the
/// operation returns. The string is left a string rather than enumerated: a sort is a
/// comma-delimited list of fields in either direction, so enumerating the legal values means
/// enumerating every ordered subset of the sortable fields: thousands of them for a handful of
/// fields, and unreadable long before that. The description carries what the caller needs
/// instead, and the binder refuses anything it does not offer.
/// </para>
/// <para>
/// The default is published in the schema as well as the prose, so a caller can see what order
/// they will get without asking for one, the answer C# will not let the parameter itself
/// carry, because a default parameter value must be a compile-time constant and no
/// <see cref="Sort"/> is one.
/// </para>
/// <para>
/// A document transformer, for the same reason filtering is one: the XML comment generator
/// matches an author's <c>&lt;param&gt;</c> text to parameters by name and runs after the
/// platform's own transformers, so anything rewriting a parameter earlier loses that text.
/// </para>
/// </remarks>
internal sealed class SortingDocumentTransformer : IOpenApiDocumentTransformer
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
                || !path.Operations.TryGetValue(HttpMethod.Parse(description.HttpMethod), out var operation)
                || operation.Parameters is null)
            {
                continue;
            }

            var declared = description.ActionDescriptor.Parameters
                .Where(parameter => parameter.ParameterType == typeof(Sort))
                .Select(parameter => parameter.Name)
                .ToArray();

            if (declared.Length == 0)
            {
                continue;
            }

            var resource = SortableFields.ResourceOf(
                Endpoints.OperationMethods.Of(description.ActionDescriptor)?.ReturnType ?? typeof(void));

            var fields = resource is null ? [] : SortableFields.Of(resource);

            for (var i = 0; i < operation.Parameters.Count; i++)
            {
                if (operation.Parameters[i].Name is { } name && declared.Contains(name, StringComparer.Ordinal))
                {
                    operation.Parameters[i] = Published(operation.Parameters[i], fields);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static OpenApiParameter Published(IOpenApiParameter declared, IReadOnlyList<SortableField> fields)
    {
        var @default = fields.SingleOrDefault(field => field.Default);
        var wire = @default is null ? null : new SortBy(@default.Name, @default.Direction).ToString();

        return new OpenApiParameter
        {
            Name = Sort.Parameter,
            In = ParameterLocation.Query,
            Required = declared.Required,
            Description = Describe(declared.Description, fields, wire),
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Default = wire is null ? null : JsonValue.Create(wire),
            },
        };
    }

    /// <remarks>
    /// The author's own <c>&lt;param&gt;</c> text leads, if they wrote any. What follows is the
    /// part no author should have to keep in step by hand: which fields exist, how to ask for
    /// them, and what happens if you do not.
    /// </remarks>
    private static string Describe(string? authored, IReadOnlyList<SortableField> fields, string? @default)
    {
        if (fields.Count == 0)
        {
            return string.IsNullOrWhiteSpace(authored)
                ? "This collection cannot be sorted."
                : authored;
        }

        var sentences = new List<string>();

        if (!string.IsNullOrWhiteSpace(authored))
        {
            sentences.Add(authored.TrimEnd());
        }

        sentences.Add(
            "Comma-delimited, in order of precedence; prefix a field with `-` to sort it descending.");

        sentences.Add(
            $"Sortable fields: {string.Join(", ", fields.Select(field => $"`{field.Name}`"))}.");

        if (@default is not null)
        {
            sentences.Add($"Defaults to `{@default}`.");
        }

        return string.Join(' ', sentences);
    }
}
