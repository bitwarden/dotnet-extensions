using System.Buffers;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class AzureServiceBusPublisher<TPayload, TCeiling> : Publisher<TPayload, TCeiling>, IAsyncDisposable
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
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

    protected internal override async Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.StartProducerActivity(_topicName, TPayload.WireName);
        _metrics.RecordPublish(_topicName, TPayload.WireName);
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.SerializeVariants<TPayload>(variants, buffer);
        var sbMessage = new ServiceBusMessage(buffer.WrittenMemory)
        {
            MessageId = Guid.NewGuid().ToString(),
        };
        sbMessage.ApplicationProperties[BrokerProperties.PayloadFamily] = TPayload.WireName;
        if (activity?.Id is { } traceId)
            sbMessage.ApplicationProperties[BrokerProperties.TraceParent] = traceId;
        try
        {
            await _sender.SendMessageAsync(sbMessage, cancellationToken);
        }
        catch (ServiceBusException ex) when (ex.Reason != ServiceBusFailureReason.MessageSizeExceeded)
        {
            throw new BrokerUnavailableException(_topicName, ex);
        }
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
