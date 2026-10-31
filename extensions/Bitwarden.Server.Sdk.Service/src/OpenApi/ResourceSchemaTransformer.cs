using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Reshapes a resource's schema so the OpenAPI document describes the wire, not the record.
/// </summary>
/// <remarks>
/// A resource is declared flat and serialised with its attributes enveloped, so the schema the
/// generator infers from the type has the wrong shape. This moves every member except <c>id</c> and
/// <c>type</c> under an <c>attributes</c> object, carrying each one's <c>required</c> status with
/// it. Descriptions and data annotations travel with the property schemas, so nothing is lost.
/// </remarks>
internal sealed class ResourceSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>
    /// Marks a schema as a resource, for <see cref="ResourceEnvelopeDocumentTransformer"/>.
    /// </summary>
    internal const string Marker = "x-bitwarden-resource";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (typeof(Resource).IsAssignableFrom(context.JsonTypeInfo.Type))
        {
            // The resource's own type name travels in the marker, because it is derivable here
            // (from the CLR type alone, with no instance) and not at the document stage, where the
            // schema has been separated from the type it came from.
            var resourceType = ResourceTypes.For(context.JsonTypeInfo.Type);

            // Marked now, restructured later. Schema transformers registered after this one (the
            // XML documentation transformer among them, registered by the service's own
            // AddOpenApi() call) still expect to find the resource's members where the record
            // declares them. Moving them here would hide them from those passes, so the move waits
            // until every schema transformer has run.
            (schema.Extensions ??= new Dictionary<string, IOpenApiExtension>())[Marker] =
                new JsonNodeExtension(resourceType);
        }

        return Task.CompletedTask;
    }
}
