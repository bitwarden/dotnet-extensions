namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Where a message type registered through the outbox goes.
/// </summary>
internal sealed record OutboxDestination(Type Payload, string Destination);
