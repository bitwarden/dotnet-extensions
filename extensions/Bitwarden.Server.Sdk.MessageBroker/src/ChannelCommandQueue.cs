using System.Buffers;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// An in-memory command queue, shared by every sender and the receiver in one process. It keeps no
/// escrow: commands still queued when the host stops are lost.
/// </summary>
internal sealed class ChannelCommandQueue : ICommandQueueReceiver
{
    private readonly Channel<CommandQueueMessage> _channel = Channel.CreateUnbounded<CommandQueueMessage>();
    private readonly string _queueName;
    private readonly int _maxDeliveryCount;
    private readonly ILogger<ChannelCommandQueue> _logger;

    public ChannelCommandQueue(string queueName, int maxDeliveryCount, ILogger<ChannelCommandQueue> logger)
    {
        _queueName = queueName;
        _maxDeliveryCount = maxDeliveryCount;
        _logger = logger;
    }

    public IAsyncEnumerable<CommandQueueMessage> ReceiveAsync(Action onSubscribed, CancellationToken cancellationToken)
    {
        // In process, the queue is there as soon as it is read.
        onSubscribed();
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<byte> body, string familyWireName, string messageId, string? traceParent,
        CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(Create(body, familyWireName, messageId, traceParent, deliveryCount: 1), cancellationToken);

    private CommandQueueMessage Create(
        ReadOnlyMemory<byte> body, string familyWireName, string messageId, string? traceParent, int deliveryCount) => new()
        {
            Body = body,
            FamilyWireName = familyWireName,
            MessageId = messageId,
            TraceParent = traceParent,
            DeliveryCount = deliveryCount,
            CompleteAsync = _ => Task.CompletedTask,
            RequeueAsync = async _ =>
            {
                if (deliveryCount >= _maxDeliveryCount)
                {
                    _logger.LogWarning(
                        "Command {MessageId} on queue {Queue} has been abandoned {DeliveryCount} times, the maximum; it will be discarded.",
                        messageId, _queueName, deliveryCount);
                    return;
                }
                await _channel.Writer.WriteAsync(
                    Create(body, familyWireName, messageId, traceParent, deliveryCount + 1), CancellationToken.None);
            },
            DeadLetterAsync = (reason, _) =>
            {
                _logger.LogWarning(
                    "Command {MessageId} on queue {Queue} was dead-lettered and will be discarded. Reason: {Reason}",
                    messageId, _queueName, reason);
                return Task.CompletedTask;
            },
        };
}

/// <summary>Sends commands of family <typeparamref name="TPayload"/> to a <see cref="ChannelCommandQueue"/>.</summary>
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
