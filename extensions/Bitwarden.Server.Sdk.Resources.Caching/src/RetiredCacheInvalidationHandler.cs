using Bitwarden.Server.Sdk.MessageBroker;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Discards a <c>cache.invalidate</c> message waiting in an outbox (<see cref="RetiredCacheInvalidation"/>), so its
/// dispatch completes rather than failing for want of a handler.
/// </summary>
/// <param name="logger">Notes each one discarded.</param>
internal sealed class RetiredCacheInvalidationHandler(ILogger<RetiredCacheInvalidationHandler> logger)
    : IMessageConsumer<CacheInvalidatePayload, CacheInvalidate>
{
    public Task HandleAsync(Envelope<CacheInvalidatePayload, CacheInvalidate> envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        logger.LogDebug(
            "Discarded a {Type} message ({MessageId}): changes invalidate the service cache just before they commit.",
            RetiredCacheInvalidation.Type,
            envelope.MessageId);

        return Task.CompletedTask;
    }
}
