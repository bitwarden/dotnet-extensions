using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Publishes an operation's <see cref="Fields{T}"/> under the name a caller sends, <c>fields[type]</c>,
/// with the attributes it may name.
/// </summary>
/// <remarks>
/// The parameter's name comes from the resource's type, not from the C# parameter, so the generator
/// would otherwise publish it as <c>fields</c>; and the attributes it accepts are the resource's,
/// which only the platform knows to list.
/// </remarks>
internal sealed class FieldsDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            // A handler's parameters, which StandardEndpointApiDescriptionProvider puts on its action descriptor.
            var declared = description.ActionDescriptor.Parameters
                .FirstOrDefault(parameter => parameter.ParameterType.IsGenericType
                    && parameter.ParameterType.GetGenericTypeDefinition() == typeof(Fields<>));

            if (declared is null
                || document.Paths is null
                || !document.Paths.TryGetValue($"/{description.RelativePath?.TrimEnd('/')}", out var path)
                || path.Operations is null
                || description.HttpMethod is null
                || !path.Operations.TryGetValue(HttpMethod.Parse(description.HttpMethod), out var operation))
            {
                continue;
            }

            var type = declared.ParameterType;
            var name = (string)type.GetProperty(nameof(Fields<JsonApi.Resource>.Parameter))!.GetValue(null)!;
            var attributes = ((IReadOnlySet<string>)type.GetProperty(nameof(Fields<JsonApi.Resource>.Attributes))!.GetValue(null)!)
                .Order(StringComparer.Ordinal);

            operation.Parameters ??= [];
            operation.Parameters = [.. operation.Parameters.Where(parameter => parameter.Name != declared.Name && parameter.Name != name)];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Query,
                Required = false,
                Description = "A sparse fieldset: the attributes to return, comma separated. Every "
                    + "attribute when omitted. One of: " + string.Join(", ", attributes) + ".",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });

            // The generator describes the C# type as a schema of its own; nothing refers to it once
            // the parameter is a string, and a caller should not be shown it.
            document.Components?.Schemas?.Remove($"FieldsOf{type.GetGenericArguments()[0].Name}");
        }

        return Task.CompletedTask;
    }
}
