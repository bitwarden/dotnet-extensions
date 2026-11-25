using System.Buffers;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Sends commands of family <typeparamref name="TPayload"/> to a <see cref="ChannelCommandQueue"/>.
/// </summary>
internal sealed class ChannelCommandSender<TPayload, TCeiling> : Publisher<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly ChannelCommandQueue _queue;
    private readonly string _queueName;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;

    public ChannelCommandSender(ChannelCommandQueue queue, string queueName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _queue = queue;
        _queueName = queueName;
        _serializer = serializer;
        _metrics = metrics;
    }

    protected internal override async Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.StartProducerActivity(_queueName, TPayload.WireName);
        _metrics.RecordPublish(_queueName, TPayload.WireName);
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.SerializeVariants<TPayload>(variants, buffer);
        await _queue.WriteAsync(buffer.WrittenMemory.ToArray(), TPayload.WireName, messageId, activity?.Id, cancellationToken);
    }
}
