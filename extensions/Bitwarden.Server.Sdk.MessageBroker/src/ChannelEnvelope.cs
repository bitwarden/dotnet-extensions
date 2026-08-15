using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelEnvelope<T> : Envelope<T>
{
    private readonly ChannelWriter<Envelope<T>> _writer;
    private readonly int _maxDeliveryCount;
    private readonly ILogger _logger;
    private readonly string _topicName;

    public ChannelEnvelope(
        ChannelWriter<Envelope<T>> writer,
        T message,
        string messageId,
        string? traceId,
        int deliveryCount,
        int maxDeliveryCount,
        ILogger logger,
        string topicName,
        Activity? activity = null)
        : base(message, activity)
    {
        _writer = writer;
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

    protected override async Task AbandonCoreAsync(CancellationToken cancellationToken)
    {
        if (DeliveryCount >= _maxDeliveryCount)
        {
            _logger.LogWarning(
                "Message {MessageId} on topic {Topic} has been abandoned {DeliveryCount} times and exceeded the maximum delivery count of {MaxDeliveryCount}. The message will be discarded.",
                MessageId, _topicName, DeliveryCount, _maxDeliveryCount);
            return;
        }
        await _writer.WriteAsync(CreateRedelivery(), cancellationToken);
    }

    private ChannelEnvelope<T> CreateRedelivery() =>
        new(_writer, Message, MessageId, TraceId, DeliveryCount + 1, _maxDeliveryCount, _logger, _topicName);
}
