using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Moves each resource schema's members under <c>attributes</c>, once every schema is complete.
/// </summary>
/// <remarks>
/// Runs at the document stage on purpose. Schema transformers see a resource's members where the
/// record declares them (flat), and some of them, notably the XML documentation transformer the
/// service's own <c>AddOpenApi()</c> call registers, need them there to attach descriptions.
/// Restructuring only after all of them have run means descriptions and data annotations are
/// already on the property schemas and travel with them into the envelope.
/// </remarks>
internal sealed class ResourceEnvelopeDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas is null)
        {
            return Task.CompletedTask;
        }

        // Materialised, because enveloping adds the attributes schema to the same collection.
        foreach (var (name, schema) in document.Components.Schemas.ToArray())
        {
            if (schema is not OpenApiSchema resource || !TryTakeResourceType(resource, out var resourceType))
            {
                continue;
            }

            Envelope(document, name, resource);
            ConstrainType(resource, resourceType);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads and removes the marker a resource schema was tagged with.
    /// </summary>
    private static bool TryTakeResourceType(OpenApiSchema schema, out string resourceType)
    {
        resourceType = string.Empty;

        if (schema.Extensions?.TryGetValue(ResourceSchemaTransformer.Marker, out var marker) != true
            || marker is not JsonNodeExtension { Node: JsonValue value }
            || value.GetValueKind() != JsonValueKind.String)
        {
            return false;
        }

        schema.Extensions.Remove(ResourceSchemaTransformer.Marker);
        resourceType = value.GetValue<string>();

        return true;
    }

    /// <summary>
    /// Pins <c>type</c> to the one value this resource may carry, and requires it.
    /// </summary>
    /// <remarks>
    /// A single-valued enum says what a free-form string cannot: that <c>type</c> is not a caller's
    /// choice. A generated client turns it into a constant rather than a parameter, and a caller
    /// that sends the wrong resource type fails against the contract rather than somewhere deeper.
    /// </remarks>
    private static void ConstrainType(OpenApiSchema schema, string resourceType)
    {
        if (schema.Properties?.TryGetValue(ResourceEnvelope.Type, out var property) != true
            || property is not OpenApiSchema typeSchema)
        {
            return;
        }

        typeSchema.Type = JsonSchemaType.String;
        typeSchema.Enum = [JsonValue.Create(resourceType)];

        (schema.Required ??= new HashSet<string>()).Add(ResourceEnvelope.Type);
    }

    /// <remarks>
    /// The attributes object is registered as a schema of its own rather than left inline. An
    /// inline object has no name, so a generator invents one from the member: every client
    /// generated from any Bitwarden service would declare a public type called
    /// <c>Attributes</c>, and a consumer of two services would have two of them.
    /// </remarks>
    private static void Envelope(OpenApiDocument document, string resourceName, OpenApiSchema schema)
    {
        if (schema.Properties is null)
        {
            return;
        }

        var attributes = schema.Properties
            .Where(property => property.Key is not (ResourceEnvelope.Id or ResourceEnvelope.Type))
            .ToArray();

        if (attributes.Length == 0)
        {
            return;
        }

        var attributeSchema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "The resource's attributes.",
            Properties = new Dictionary<string, IOpenApiSchema>(),
        };

        foreach (var (name, property) in attributes)
        {
            attributeSchema.Properties[name] = property;
            schema.Properties.Remove(name);

            if (schema.Required?.Remove(name) == true)
            {
                (attributeSchema.Required ??= new HashSet<string>()).Add(name);
            }
        }

        var attributesName = $"{resourceName}Attributes";
        document.Components!.Schemas![attributesName] = attributeSchema;

        schema.Properties[ResourceEnvelope.Attributes] = new OpenApiSchemaReference(attributesName, document);
        (schema.Required ??= new HashSet<string>()).Add(ResourceEnvelope.Attributes);
    }
}
