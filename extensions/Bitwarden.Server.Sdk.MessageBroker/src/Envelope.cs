using System.Diagnostics;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// A received payload paired with its lifecycle controls
/// </summary>
/// <typeparam name="TPayload">The payload family.</typeparam>
/// <typeparam name="TCeiling">The variant the consumer knows how to handle.</typeparam>
public abstract class Envelope<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly Activity? _activity;
    private readonly Dictionary<Type, Payload<TPayload>.IVariant> _variantsByType;
    private bool _settled;

    /// <param name="variants">
    /// The variants the publisher sent. Order is not required — lookups walk the declared chain,
    /// not the wire, so duplicates or out-of-order entries collapse safely. May omit variants
    /// the subscriber cannot deserialize (unknown types are skipped).
    /// </param>
    /// <param name="activity">Consumer span opened when the envelope was received; disposed on settlement.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TCeiling"/> is neither present in <paramref name="variants"/>
    /// nor reachable by pure upcast from a lower received variant. Concrete transports catch this
    /// at construction and dead-letter the message so the consumer never sees an envelope without
    /// a usable <see cref="Payload"/>.
    /// </exception>
    internal Envelope(IEnumerable<Payload<TPayload>.IVariant> variants, Activity? activity = null)
    {
        _variantsByType = [];
        foreach (var variant in variants)
            _variantsByType[variant.GetType()] = variant;
        _activity = activity;
        Payload = ResolvePayload();
    }

    /// <summary>
    /// Every variant the subscriber could decode from the received message. Framework-internal —
    /// the escrow store re-serializes these on shutdown. Consumers reach into the message data
    /// via <see cref="Payload"/>.
    /// </summary>
    internal IEnumerable<Payload<TPayload>.IVariant> Variants => _variantsByType.Values;

    /// <summary>
    /// The variant the consumer knows how to handle: a direct match if one arrived, otherwise the
    /// result of walking pure upcasts from the highest received variant at or below the ceiling.
    /// </summary>
    public TCeiling Payload { get; }

    private TCeiling ResolvePayload()
    {
        // Walk down the declared chain from TCeiling. The first position with a received variant
        // is the highest one at or below the ceiling; upcast that variant back up to TCeiling via
        // IPureUp — every non-ceiling variant in a validated chain has a pure upcast.
        for (Type? cur = typeof(TCeiling); cur is not null; cur = ChainWalk.PreviousInChain(cur))
        {
            if (!_variantsByType.TryGetValue(cur, out var variant)) continue;

            var current = variant;
            while (current is not TCeiling && current is Payload<TPayload>.IPureUp step)
                current = step.Upcast();

            if (current is TCeiling reached) return reached;
        }

        throw new InvalidOperationException(
            $"No variant of type {typeof(TCeiling).Name} could be produced from the received payload. " +
            $"The received variants ({string.Join(", ", _variantsByType.Keys.Select(t => t.Name))}) " +
            "do not include the requested type and cannot be upcast to it.");
    }

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
    /// May be called from inside <see cref="IMessageConsumer{TPayload, TCeiling}.HandleAsync"/>
    /// when the consumer determines a message is permanently unprocessable. The framework will
    /// skip its own settlement after
    /// <see cref="IMessageConsumer{TPayload, TCeiling}.HandleAsync"/> returns or throws.
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
