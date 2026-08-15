using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class ChannelPublisher<T> : IPublisher<T>
{
    private readonly ChannelTopic<T> _topic;
    private readonly string _topicName;
    private readonly MessageBrokerMetrics _metrics;
    private readonly int _maxDeliveryCount;
    private readonly ILogger<ChannelPublisher<T>> _logger;

    public ChannelPublisher(ChannelTopic<T> topic, string topicName, MessageBrokerMetrics metrics, int maxDeliveryCount, ILogger<ChannelPublisher<T>> logger)
    {
        _topic = topic;
        _topicName = topicName;
        _metrics = metrics;
        _maxDeliveryCount = maxDeliveryCount;
        _logger = logger;
    }

    public async Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        using var activity = MessageBrokerActivitySource.Source.StartActivity(
            $"{_topicName} publish", ActivityKind.Producer);
        _metrics.RecordPublish(_topicName);
        var messageId = Guid.NewGuid().ToString();
        var traceId = activity?.Id;
        await _topic.WriteAsync(
            writer =>
            {
                var consumerActivity = MessageBrokerActivitySource.StartConsumerActivity(_topicName, traceId);
                return new ChannelEnvelope<T>(writer, message, messageId, traceId,
                    deliveryCount: 1, _maxDeliveryCount, _logger, _topicName, consumerActivity);
            },
            cancellationToken);
        // Producer span closes here. Each subscriber group received its own consumer span
        // (started above in the factory) that lives until the envelope is settled.
    }
}
