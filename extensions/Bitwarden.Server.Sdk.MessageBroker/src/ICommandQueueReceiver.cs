namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives every message on one command queue, whatever its payload family.
/// </summary>
internal interface ICommandQueueReceiver
{
    /// <summary>
    /// Returns the queue's messages as they arrive, one unsettled message at a time.
    /// </summary>
    IAsyncEnumerable<CommandQueueMessage> ReceiveAsync(CancellationToken cancellationToken);
}
