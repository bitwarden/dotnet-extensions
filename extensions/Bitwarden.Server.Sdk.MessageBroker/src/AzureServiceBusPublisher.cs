using System.Buffers;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class AzureServiceBusPublisher<TFamily, TCeiling> : Publisher<TFamily, TCeiling>, IAsyncDisposable
    where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>, IFamilyWireName<TFamily>
    where TCeiling : Family<TFamily>.ICeiling
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
        IReadOnlyList<Family<TFamily>.IVariant> variants,
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.StartProducerActivity(_topicName, TFamily.WireName);
        _metrics.RecordPublish(_topicName, TFamily.WireName);
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.SerializeVariants<TFamily>(variants, buffer);
        var sbMessage = new ServiceBusMessage(buffer.WrittenMemory)
        {
            MessageId = Guid.NewGuid().ToString(),
        };
        sbMessage.ApplicationProperties[BrokerProperties.Family] = TFamily.WireName;
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
