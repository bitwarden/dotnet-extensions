namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// A seam for a test harness: watches what the dispatcher hands to the broker. Never registered in
/// a running service.
/// </summary>
/// <remarks>
/// Only messages bound for the broker pass it, never those handled in the sending service itself.
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
