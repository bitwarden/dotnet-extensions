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
    private readonly Task<IChannel> _channel;

    public RabbitPublisher(RabbitConnection connection, string exchangeName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _exchangeName = exchangeName;
        _serializer = serializer;
        _metrics = metrics;
        // Begin creating the channel as soon as the connection is available.
        // By the time any traffic arrives the hosted service will have completed StartAsync,
        // so this task is almost always already finished.
        _channel = OpenChannelAsync(connection);
    }

    private static async Task<IChannel> OpenChannelAsync(RabbitConnection connection)
    {
        var conn = await connection.GetConnectionAsync();
        // Publisher confirms with tracking: BasicPublishAsync completes only once the broker has
        // taken responsibility for the message, and throws PublishException if it refuses it. A
        // send that returns has been accepted, which is what lets a caller such as an outbox
        // treat it as delivered to the broker.
        return await conn.CreateChannelAsync(new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true));
    }

    protected internal override async Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        CancellationToken cancellationToken)
    {
        using var activity = MessageBrokerActivitySource.StartProducerActivity(_exchangeName, TPayload.WireName);
        _metrics.RecordPublish(_exchangeName, TPayload.WireName);
        try
        {
            var channel = await _channel;
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
            // The broker refused the message (a nack), for instance a full queue that rejects publishes.
            // mandatory is false, so an unroutable message is not a refusal: an event topic
            // with no subscriptions accepts and drops it, as on Azure Service Bus.
            throw new BrokerUnavailableException(_exchangeName, ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel.IsCompletedSuccessfully)
            await _channel.Result.DisposeAsync();
    }
}
