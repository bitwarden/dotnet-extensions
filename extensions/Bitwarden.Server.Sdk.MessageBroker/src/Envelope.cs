using System.Diagnostics;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Wraps a received message and controls its lifecycle.</summary>
/// <typeparam name="T">The message type.</typeparam>
public abstract class Envelope<T>
{
    private readonly Activity? _activity;

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
        _activity?.Dispose();
        return CompleteAsyncCore(cancellationToken);
    }

    /// <inheritdoc cref="CompleteAsync"/>
    protected abstract Task CompleteAsyncCore(CancellationToken cancellationToken);

    /// <summary>
    /// Abandons the message, returning it to the queue for redelivery, and marks the
    /// message's activity as failed with <paramref name="reason"/>.
    /// </summary>
    public Task AbandonAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        _activity?.SetStatus(ActivityStatusCode.Error, reason);
        _activity?.Dispose();
        return AbandonCoreAsync(cancellationToken);
    }

    /// <inheritdoc cref="AbandonAsync"/>
    protected abstract Task AbandonCoreAsync(CancellationToken cancellationToken);
}
