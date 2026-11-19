namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Thrown by a route to set its message aside.
/// </summary>
internal sealed class OutboxMessageSetAsideException(string message, Exception? inner = null) : Exception(message, inner);
