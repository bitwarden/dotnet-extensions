namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// An in-flight message captured during host shutdown, ready to be durably stored and replayed
/// after the process restarts.
/// </summary>
/// <param name="MessageId">Unique identifier assigned by the publisher.</param>
/// <param name="TraceId">W3C traceparent of the publish span, or <see langword="null"/> if unavailable.</param>
/// <param name="DeliveryCount">Number of times this message has been delivered before escrow.</param>
/// <param name="Payload">
/// The message body serialized by the configured <see cref="IMessageSerializer"/>. The store may
/// persist these bytes as-is (e.g. a blob column) or re-encode them further.
/// </param>
public sealed record EscrowedMessage(string MessageId, string? TraceId, int DeliveryCount, byte[] Payload);

/// <summary>
/// Durably stores in-flight channel messages during host shutdown so they can be replayed after
/// the process restarts.
/// </summary>
/// <remarks>
/// Register an implementation with the DI container to enable durable escrow for the in-memory
/// channel backend. Without a registered implementation, undelivered messages are logged as errors.
/// The escrow store is only used when the channel backend is active — Azure Service Bus and RabbitMQ
/// backends manage message durability on the broker side.
/// </remarks>
public interface IMessageEscrowStore
{
    /// <summary>
    /// Atomically stores <paramref name="messages"/> under <paramref name="key"/>.
    /// Called during host shutdown after the channel writers have been sealed.
    /// </summary>
    Task WriteAsync(string key, IReadOnlyList<EscrowedMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all previously stored messages for <paramref name="key"/> and atomically removes them.
    /// Called during host startup before consumers begin processing. Returns an empty list when no
    /// messages are stored for the key.
    /// </summary>
    Task<IReadOnlyList<EscrowedMessage>> ReadAndClearAsync(string key, CancellationToken cancellationToken = default);
}
