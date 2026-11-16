using System.Buffers;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class RabbitPublisher<TPayload, TCeiling> : Publisher<TPayload, TCeiling>, IAsyncDisposable
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly string _destinationName;
    private readonly string _exchangeName;
    private readonly string _routingKey;
    private readonly bool _mandatory;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;
    private readonly RabbitConnection _connection;
    private readonly SemaphoreSlim _channelGate = new(1, 1);
    private IChannel? _channel;

    /// <param name="connection">The shared broker connection.</param>
    /// <param name="destinationName">The topic or command queue, as spans, metrics and errors name it.</param>
    /// <param name="exchangeName">
    /// The exchange to publish to: the topic's exchange, or the command queue's own exchange, which shares
    /// its name and is bound only to it.
    /// </param>
    /// <param name="routingKey">The family wire name for a topic, or the queue's name for a command queue.</param>
    /// <param name="mandatory">
    /// Whether a message no queue receives is refused. False for a topic, whose publisher doesn't
    /// depend on who is listening; true for a command queue, whose command would otherwise be lost.
    /// </param>
    /// <param name="serializer">Serializes the variants.</param>
    /// <param name="metrics">Records the publish.</param>
    public RabbitPublisher(
        RabbitConnection connection, string destinationName, string exchangeName, string routingKey, bool mandatory,
        IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _destinationName = destinationName;
        _exchangeName = exchangeName;
        _routingKey = routingKey;
        _mandatory = mandatory;
        _serializer = serializer;
        _metrics = metrics;
        _connection = connection;
    }

    // Opens the channel on first use, and again whenever it isn't open: the broker closed it, or
    // the connection under it was lost and has to be opened again.
    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
            return open;

        // Wait for the connection before taking the gate: concurrent sends then share one attempt
        // to connect (see RabbitConnection) instead of queueing behind each other's attempts.
        var conn = await _connection.GetConnectionAsync(cancellationToken);

        await _channelGate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true } opened)
                return opened;
            if (_channel is not null)
            {
                try
                {
                    await _channel.DisposeAsync();
                }
                catch (Exception)
                {
                    // A closed channel can fail to dispose; nothing on it is worth recovering.
                }
                _channel = null;
            }

            // Publisher confirms with tracking: BasicPublishAsync completes only once the broker
            // has taken responsibility for the message, and throws PublishException if it refuses
            // it. A send that returns has been accepted, which is what lets a caller such as an
            // outbox treat it as delivered to the broker.
            _channel = await conn.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);
            return _channel;
        }
        finally
        {
            _channelGate.Release();
        }
    }

    protected internal override async Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.SerializeVariants<TPayload>(variants, buffer);
        await SendRawAsync(buffer.WrittenMemory, messageId, cancellationToken);
    }

    internal override async Task SendRawAsync(ReadOnlyMemory<byte> body, string messageId, CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.StartProducerActivity(_destinationName, TPayload.WireName);
        _metrics.RecordPublish(_destinationName, TPayload.WireName);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var props = new BasicProperties
            {
                MessageId = messageId,
                Headers = new Dictionary<string, object?>
                {
                    [BrokerProperties.TraceParent] = activity?.Id,
                    [BrokerProperties.PayloadFamily] = TPayload.WireName,
                },
            };
            await channel.BasicPublishAsync(_exchangeName, _routingKey, _mandatory,
                props, body, cancellationToken);
        }
        catch (BrokerUnreachableException ex)
        {
            throw new BrokerUnavailableException(_destinationName, ex);
        }
        catch (OperationInterruptedException ex)
        {
            // Includes a command queue whose exchange doesn't exist yet: the broker closes the
            // channel, and the next send opens a new one.
            throw new BrokerUnavailableException(_destinationName, ex);
        }
        catch (PublishException ex)
        {
            // A nack: the broker refused the message, for instance a full queue that rejects publishes. Or a
            // return (IsReturn), only possible when mandatory: no queue would receive a command,
            // because the receiver's queue isn't bound to its exchange yet. A retry can succeed once it is,
            // as on Azure Service Bus, where sending to a missing queue fails the same way.
            throw new BrokerUnavailableException(_destinationName, ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
    }
}
