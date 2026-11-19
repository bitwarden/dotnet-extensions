namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Thrown by a route whose handler asked for its message to be tried again later.
/// </summary>
internal sealed class OutboxMessageRequeuedException(string message) : Exception(message);
