using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelPublisher<TPayload, TCeiling> : Publisher<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly ChannelTopic<TPayload, TCeiling> _topic;
    private readonly string _topicName;
    private readonly MessageBrokerMetrics _metrics;
    private readonly int _maxDeliveryCount;
    private readonly ILogger<ChannelPublisher<TPayload, TCeiling>> _logger;

    public ChannelPublisher(
        ChannelTopic<TPayload, TCeiling> topic,
        string topicName,
        MessageBrokerMetrics metrics,
        int maxDeliveryCount,
        ILogger<ChannelPublisher<TPayload, TCeiling>> logger)
    {
        _topic = topic;
        _topicName = topicName;
        _metrics = metrics;
        _maxDeliveryCount = maxDeliveryCount;
        _logger = logger;
    }

    protected internal override async Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.Source.StartActivity(
            $"{_topicName} publish", ActivityKind.Producer);
        _metrics.RecordPublish(_topicName);
        var messageId = Guid.NewGuid().ToString();
        var traceId = activity?.Id;
        await _topic.WriteAsync(
            (writer, escrowFallback) =>
            {
                var consumerActivity = MessageBrokerActivitySource.StartConsumerActivity(_topicName, traceId);
                return new ChannelEnvelope<TPayload, TCeiling>(writer, escrowFallback, variants, messageId, traceId,
                    deliveryCount: 1, _maxDeliveryCount, _logger, _topicName, consumerActivity);
            },
            cancellationToken);
        // Producer span closes here. Each subscriber group received its own consumer span
        // (started above in the factory) that lives until the envelope is settled.
    }
}
