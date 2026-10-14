using System.Buffers;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class AzureServiceBusPublisher<T> : IPublisher<T>, IAsyncDisposable
{
    private readonly string _topicName;
    private readonly ServiceBusSender _sender;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;

    public AzureServiceBusPublisher(ServiceBusClient client, string topicName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _topicName = topicName;
        _sender = client.CreateSender(topicName);
        _serializer = serializer;
        _metrics = metrics;
    }

    public async Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        using var activity = MessageBrokerActivitySource.Source.StartActivity(
            $"{_topicName} publish", ActivityKind.Producer);
        _metrics.RecordPublish(_topicName);
        // TODO: Use an ArrayPool<byte>.Shared based buffer writer for extra performance.
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(message, buffer);
        var sbMessage = new ServiceBusMessage(buffer.WrittenMemory)
        {
            MessageId = Guid.NewGuid().ToString(),
        };
        if (activity?.Id is { } traceId)
            sbMessage.ApplicationProperties["traceparent"] = traceId;
        try
        {
            await _sender.SendMessageAsync(sbMessage, cancellationToken);
        }
        catch (ServiceBusException ex) when (ex.Reason != ServiceBusFailureReason.MessageSizeExceeded)
        {
            throw new BrokerUnavailableException(_topicName, ex);
        }
    }

    public async Task PublishBatchAsync(IEnumerable<T> messages, CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();
        using var activity = MessageBrokerActivitySource.Source.StartActivity(
            $"{_topicName} publish", ActivityKind.Producer);
        _metrics.RecordPublish(_topicName, messageList.Count);
        var traceId = activity?.Id;
        var sbMessages = messageList.Select(m =>
        {
            var buffer = new ArrayBufferWriter<byte>();
            _serializer.Serialize(m, buffer);
            var sbMessage = new ServiceBusMessage(buffer.WrittenMemory)
            {
                MessageId = Guid.NewGuid().ToString(),
            };
            if (traceId is not null)
                sbMessage.ApplicationProperties["traceparent"] = traceId;
            return sbMessage;
        });
        try
        {
            await _sender.SendMessagesAsync(sbMessages, cancellationToken);
        }
        catch (ServiceBusException ex) when (ex.Reason != ServiceBusFailureReason.MessageSizeExceeded)
        {
            throw new BrokerUnavailableException(_topicName, ex);
        }
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
