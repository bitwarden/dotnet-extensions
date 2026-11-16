namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Watches what the dispatcher hands to the broker: a seam for a test harness, which records each
/// message sent and can stand in for a broker that is down. Never registered in a running service.
/// </summary>
/// <remarks>
/// Only messages bound for the broker pass it, never those handled in the sending service itself
/// (<see cref="MessageOutbox.Local"/>). The row is the stored one: its body is the serialized
/// versions, as written.
/// </remarks>
internal interface IOutboxSendObserver
{
    /// <summary>
    /// Called before the message is sent. A throw fails the send as the broker's own failure would:
    /// the message stays in the outbox and is tried again later.
    /// </summary>
    void Sending(OutboxMessage message);

    /// <summary>
    /// Called once the broker has the message, before its row is deleted.
    /// </summary>
    void Sent(OutboxMessage message);
}
