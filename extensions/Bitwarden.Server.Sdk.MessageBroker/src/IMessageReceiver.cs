namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives every message on one queue or topic subscription, whatever its payload family.
/// </summary>
internal interface IMessageReceiver
{
    /// <summary>
    /// Returns the queue's messages as they arrive, one unsettled message at a time.
    /// </summary>
    IAsyncEnumerable<ReceivedMessage> ReceiveAsync(CancellationToken cancellationToken);
}
