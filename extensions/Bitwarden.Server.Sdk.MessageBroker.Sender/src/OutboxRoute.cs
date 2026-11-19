namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// How the dispatcher sends a waiting message of one type to one destination.
/// </summary>
internal abstract class OutboxRoute
{
    /// <summary>
    /// The topic or command queue.
    /// </summary>
    public abstract string Destination { get; }

    /// <summary>
    /// The message type.
    /// </summary>
    public abstract string MessageType { get; }

    /// <summary>
    /// Sends <paramref name="message"/>, returning once the broker has it or its handler is done. A
    /// throw leaves it in the outbox to try again later, or sets it aside when sending again can't
    /// succeed (<see cref="OutboxFailures.IsPermanent"/>).
    /// </summary>
    public abstract Task SendAsync(OutboxMessage message, CancellationToken cancellationToken);
}
