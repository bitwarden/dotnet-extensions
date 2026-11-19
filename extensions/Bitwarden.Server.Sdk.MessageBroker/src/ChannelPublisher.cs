using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelPublisher<TPayload, TCeiling> : Publisher<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly ChannelTopic<TPayload, TCeiling> _topic;
    private readonly string _topicName;
    private readonly MessageBrokerMetrics _metrics;
    private readonly int _maxDeliveryCount;
    private readonly ILogger<ChannelPublisher<TPayload, TCeiling>> _logger;
    private readonly IMessageSerializer _serializer;

    public ChannelPublisher(
        ChannelTopic<TPayload, TCeiling> topic,
        string topicName,
        MessageBrokerMetrics metrics,
        int maxDeliveryCount,
        ILogger<ChannelPublisher<TPayload, TCeiling>> logger,
        IMessageSerializer serializer)
    {
        _serializer = serializer;
        _topic = topic;
        _topicName = topicName;
        _metrics = metrics;
        _maxDeliveryCount = maxDeliveryCount;
        _logger = logger;
    }

    protected internal override async Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.StartProducerActivity(_topicName, TPayload.WireName);
        _metrics.RecordPublish(_topicName, TPayload.WireName);
        var traceId = activity?.Id;
        await _topic.WriteAsync(
            (writer, escrowFallback) =>
            {
                var consumerActivity = MessageBrokerActivitySource.StartConsumerActivity(_topicName, TPayload.WireName, traceId);
                return new ChannelEnvelope<TPayload, TCeiling>(writer, escrowFallback, variants, messageId, traceId,
                    deliveryCount: 1, _maxDeliveryCount, _logger, _topicName, consumerActivity);
            },
            cancellationToken);
        // Producer span closes here. Each subscriber group received its own consumer span
        // (started above in the factory) that lives until the envelope is settled.
    }

    // In process, every stored version is one this build knows.
    internal override Task SendRawAsync(ReadOnlyMemory<byte> body, string messageId, CancellationToken cancellationToken) =>
        SendAsync(_serializer.DeserializeVariants<TPayload>(body.Span), messageId, cancellationToken);
}
