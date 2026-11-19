namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// A route for one message type and destination, and the outbox it is sent from.
/// </summary>
internal sealed record OutboxRegistration(Type Context, OutboxRoute Route);
