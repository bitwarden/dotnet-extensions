using System.Diagnostics;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Wraps a received message and controls its lifecycle.</summary>
/// <typeparam name="T">The message type.</typeparam>
public abstract class Envelope<T>
{
    private readonly Activity? _activity;
    private bool _settled;

    internal Envelope(T message, Activity? activity = null)
    {
        Message = message;
        _activity = activity;
    }

    /// <summary>The received message.</summary>
    public T Message { get; }

    /// <summary>A unique identifier for the message assigned by the publisher.</summary>
    public abstract string MessageId { get; }

    /// <summary>
    /// The W3C traceparent of the publish span, or <see langword="null"/> if the message
    /// was published without an active trace. Use this to link the consumer span to the
    /// producer span in distributed traces.
    /// </summary>
    public abstract string? TraceId { get; }

    /// <summary>
    /// The number of times this message has been delivered. One on the first delivery attempt,
    /// incrementing with each redelivery. Matches Azure Service Bus's <c>DeliveryCount</c> property
    /// and is one greater than Rabbit's zero-based <c>x-delivery-count</c> header.
    /// </summary>
    public abstract int DeliveryCount { get; }

    /// <summary>Acknowledges the message, removing it from the queue permanently.</summary>
    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_settled) return Task.CompletedTask;
        _settled = true;
        _activity?.Dispose();
        return CompleteAsyncCore(cancellationToken);
    }

    /// <inheritdoc cref="CompleteAsync"/>
    protected abstract Task CompleteAsyncCore(CancellationToken cancellationToken);

    /// <summary>
    /// Abandons the message, returning it to the queue for redelivery, and marks the
    /// message's activity as failed with <paramref name="reason"/>.
    /// </summary>
    public Task RequeueAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (_settled) return Task.CompletedTask;
        _settled = true;
        _activity?.SetStatus(ActivityStatusCode.Error, reason);
        _activity?.Dispose();
        return RequeueCoreAsync(cancellationToken);
    }

    /// <inheritdoc cref="RequeueAsync"/>
    protected abstract Task RequeueCoreAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Permanently removes the message without redelivery. On Azure Service Bus and RabbitMQ this
    /// routes the message to the broker's dead-letter queue or exchange; on the in-memory channel
    /// backend the message is discarded with a warning log.
    /// </summary>
    /// <remarks>
    /// May be called from inside <see cref="IMessageConsumer{T}.HandleAsync"/> when the consumer
    /// determines a message is permanently unprocessable. The framework will skip its own
    /// settlement after <see cref="IMessageConsumer{T}.HandleAsync"/> returns or throws.
    /// </remarks>
    public Task DeadLetterAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        if (_settled) return Task.CompletedTask;
        _settled = true;
        _activity?.SetStatus(ActivityStatusCode.Error, reason);
        _activity?.Dispose();
        return DeadLetterAsyncCore(reason, cancellationToken);
    }

    /// <inheritdoc cref="DeadLetterAsync"/>
    protected abstract Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken);
}
