using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.OpenApi;

/// <summary>
/// Publishes each operation that declares <see cref="AcceptsIdempotencyKeyAttribute"/> as
/// <c>"x-bitwarden-idempotency-key": true</c>, so a client package built from the contract sends an
/// <c>Idempotency-Key</c> to that operation, and only to it.
/// </summary>
internal sealed class IdempotencyKeyOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<AcceptsIdempotencyKeyAttribute>().Any())
        {
            operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            operation.Extensions[IdempotencyKeys.Extension] = new JsonNodeExtension(JsonValue.Create(true));
        }

        return Task.CompletedTask;
    }
}
