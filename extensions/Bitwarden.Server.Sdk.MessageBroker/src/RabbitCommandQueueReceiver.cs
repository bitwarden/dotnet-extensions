using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives every message on a RabbitMQ command queue, which senders reach through its own exchange.
/// </summary>
internal sealed class RabbitCommandQueueReceiver : IMessageReceiver
{
    private readonly RabbitConnection _connection;
    private readonly string _queueName;

    public RabbitCommandQueueReceiver(RabbitConnection connection, string queueName)
    {
        _connection = connection;
        _queueName = queueName;
    }

    public async IAsyncEnumerable<ReceivedMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The host closing the connection during setup is a shutdown, not a broker failure.
        using var setupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connection.Closed);
        IChannel rabbitChannel;
        try
        {
            var conn = await _connection.GetConnectionAsync(setupCancellation.Token);
            rabbitChannel = await conn.CreateChannelAsync(cancellationToken: setupCancellation.Token);
        }
        catch (Exception) when (_connection.IsClosing && !cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        await using var channelLifetime = rabbitChannel;

        var innerChannel = Channel.CreateUnbounded<ReceivedMessage>();

        var consumer = new AsyncEventingBasicConsumer(rabbitChannel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var headers = ea.BasicProperties.Headers;
            var deliveryTag = ea.DeliveryTag;
            var deliveryCount = RabbitDeliveryCount.Read(headers, ea.Redelivered);
            await innerChannel.Writer.WriteAsync(new ReceivedMessage
            {
                // The body's memory belongs to the client and is reused once this handler returns.
                Body = ea.Body.ToArray(),
                FamilyWireName = ReadHeader(headers, BrokerProperties.PayloadFamily),
                MessageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString(),
                TraceParent = ReadHeader(headers, BrokerProperties.TraceParent),
                DeliveryCount = deliveryCount,
                CompleteAsync = ct => rabbitChannel.BasicAckAsync(deliveryTag, multiple: false, ct).AsTask(),
                // Reject, not nack: RabbitMQ 4.3+ counts only a reject toward the delivery limit.
                RequeueAsync = ct => rabbitChannel.IsOpen
                    ? rabbitChannel.BasicRejectAsync(deliveryTag, requeue: true, ct).AsTask()
                    : Task.CompletedTask,
                // RabbitMQ records no reason on a nack; the queue's dead-letter policy decides where it goes.
                DeadLetterAsync = (_, ct) => rabbitChannel.IsOpen
                    ? rabbitChannel.BasicNackAsync(deliveryTag, multiple: false, requeue: false, ct).AsTask()
                    : Task.CompletedTask,
            }, CancellationToken.None);
        };

        Exception? brokerDisconnect = null;
        rabbitChannel.ChannelShutdownAsync += (_, args) =>
        {
            if (args.Initiator != ShutdownInitiator.Application)
            {
                brokerDisconnect = new Exception(args.ReplyText);
            }

            innerChannel.Writer.TryComplete();
            return Task.CompletedTask;
        };

        try
        {
            await rabbitChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: setupCancellation.Token);
            await rabbitChannel.BasicConsumeAsync(_queueName, autoAck: false, consumer: consumer, cancellationToken: setupCancellation.Token);
        }
        catch (Exception) when (_connection.IsClosing && !cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        await foreach (var message in innerChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }

        if (brokerDisconnect is not null)
        {
            throw new BrokerDisconnectedException(_queueName, brokerDisconnect);
        }
    }

    private static string? ReadHeader(IDictionary<string, object?>? headers, string name) =>
        headers?.TryGetValue(name, out var value) == true && value is byte[] bytes
            ? Encoding.UTF8.GetString(bytes)
            : null;
}
