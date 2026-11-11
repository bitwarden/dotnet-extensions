using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Resources.OpenApi;

/// <summary>
/// Marks a resource's schema so <see cref="ResourceEnvelopeDocumentTransformer"/> can envelope it
/// once every schema transformer has run.
/// </summary>
/// <remarks>
/// The marker carries the resource's type name, which is derivable from the CLR type here but not
/// at the document stage. Members stay flat until then so later schema transformers (XML docs among
/// them) still find them where the record declares them.
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
