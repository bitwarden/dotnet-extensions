namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives every message on one queue or topic subscription, whatever its payload family.
/// </summary>
internal interface IMessageReceiver
{
    /// <summary>
    /// Returns the queue's messages as they arrive, one unsettled message at a time, calling
    /// <paramref name="onSubscribed"/> once the receiver is in place, whether or not a message comes.
    /// </summary>
    IAsyncEnumerable<ReceivedMessage> ReceiveAsync(Action onSubscribed, CancellationToken cancellationToken);
}
