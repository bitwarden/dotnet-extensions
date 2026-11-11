using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelEnvelope<TFamily, TCeiling> : Envelope<TFamily, TCeiling>
    where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>
    where TCeiling : Family<TFamily>.ICeiling
{
    private readonly ChannelWriter<Envelope<TFamily, TCeiling>> _writer;
    private readonly Func<Envelope<TFamily, TCeiling>, CancellationToken, Task>? _escrowFallback;
    private readonly int _maxDeliveryCount;
    private readonly ILogger _logger;
    private readonly string _topicName;

    public ChannelEnvelope(
        ChannelWriter<Envelope<TFamily, TCeiling>> writer,
        Func<Envelope<TFamily, TCeiling>, CancellationToken, Task>? escrowFallback,
        IEnumerable<Family<TFamily>.IVariant> variants,
        string messageId,
        string? traceId,
        int deliveryCount,
        int maxDeliveryCount,
        ILogger logger,
        string topicName,
        Activity? activity = null)
        : base(variants, activity)
    {
        _writer = writer;
        _escrowFallback = escrowFallback;
        _maxDeliveryCount = maxDeliveryCount;
        _logger = logger;
        _topicName = topicName;
        MessageId = messageId;
        TraceId = traceId;
        DeliveryCount = deliveryCount;
    }

    public override string MessageId { get; }
    public override string? TraceId { get; }
    public override int DeliveryCount { get; }

    protected override Task CompleteAsyncCore(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken)
    {
        // TODO: Investigate storing dead-lettered messages in the escrow store so they can be
        // inspected and replayed, rather than being discarded.
        _logger.LogWarning(
            "Message {MessageId} on topic {Topic} was dead-lettered and will be discarded. Reason: {Reason}",
            MessageId, _topicName, reason);
        return Task.CompletedTask;
    }

    protected override Task RequeueCoreAsync(CancellationToken cancellationToken)
    {
        if (DeliveryCount >= _maxDeliveryCount)
        {
            _logger.LogWarning(
                "Message {MessageId} on topic {Topic} has been abandoned {DeliveryCount} times and exceeded the maximum delivery count of {MaxDeliveryCount}. The message will be discarded.",
                MessageId, _topicName, DeliveryCount, _maxDeliveryCount);
            return Task.CompletedTask;
        }

        var redelivery = CreateRedelivery();
        // TryWrite returns false only when the writer is completed (the channel is unbounded so
        // capacity is never the reason). When the writer is closed during host shutdown, send
        // the message straight to escrow via the registered fallback so it survives regardless
        // of the order in which ChannelTopic and ChannelEscrowService stop.
        if (!_writer.TryWrite(redelivery))
        {
            if (_escrowFallback is not null)
                return _escrowFallback(redelivery, cancellationToken);

            _logger.LogWarning(
                "Message {MessageId} on topic {Topic} was abandoned during shutdown with no escrow configured; the message will be lost.",
                MessageId, _topicName);
        }

        return Task.CompletedTask;
    }

    private ChannelEnvelope<TFamily, TCeiling> CreateRedelivery() =>
        new(_writer, _escrowFallback, Variants, MessageId, TraceId, DeliveryCount + 1, _maxDeliveryCount, _logger, _topicName);
}
