using System.Buffers;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class AzureServiceBusPublisher<TPayload, TCeiling> : Publisher<TPayload, TCeiling>, IAsyncDisposable
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly string _topicName;
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;

    public AzureServiceBusPublisher(string connectionString, string topicName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _topicName = topicName;
        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(topicName);
        _serializer = serializer;
        _metrics = metrics;
    }

    protected internal override async Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.Source.StartActivity(
            $"{_topicName} publish", ActivityKind.Producer);
        _metrics.RecordPublish(_topicName);
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.SerializeVariants<TPayload>(variants, buffer);
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

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}
