using System.Buffers;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class RabbitPublisher<TPayload, TCeiling> : Publisher<TPayload, TCeiling>, IAsyncDisposable
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly string _exchangeName;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;
    private readonly RabbitConnection _connection;
    private readonly SemaphoreSlim _channelGate = new(1, 1);
    private IChannel? _channel;

    public RabbitPublisher(RabbitConnection connection, string exchangeName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _exchangeName = exchangeName;
        _serializer = serializer;
        _metrics = metrics;
        _connection = connection;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
            return open;

        // Before the gate, so concurrent sends share one attempt to connect.
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
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.StartProducerActivity(_exchangeName, TPayload.WireName);
        _metrics.RecordPublish(_exchangeName, TPayload.WireName);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var buffer = new ArrayBufferWriter<byte>();
            _serializer.SerializeVariants<TPayload>(variants, buffer);
            var props = new BasicProperties
            {
                MessageId = Guid.NewGuid().ToString(),
                Headers = new Dictionary<string, object?>
                {
                    [BrokerProperties.TraceParent] = activity?.Id,
                    [BrokerProperties.PayloadFamily] = TPayload.WireName,
                },
            };
            await channel.BasicPublishAsync(_exchangeName, routingKey: TPayload.WireName, mandatory: false,
                props, buffer.WrittenMemory, cancellationToken);
        }
        catch (BrokerUnreachableException ex)
        {
            throw new BrokerUnavailableException(_exchangeName, ex);
        }
        catch (OperationInterruptedException ex)
        {
            throw new BrokerUnavailableException(_exchangeName, ex);
        }
        catch (PublishException ex)
        {
            throw new BrokerUnavailableException(_exchangeName, ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();
    }
}
