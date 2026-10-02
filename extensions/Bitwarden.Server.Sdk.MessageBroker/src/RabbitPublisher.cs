using System.Buffers;
using System.Diagnostics;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class RabbitPublisher<T> : IPublisher<T>, IAsyncDisposable
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
        return await conn.CreateChannelAsync();
    }

    public async Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        using var activity = MessageBrokerActivitySource.Source.StartActivity(
            $"{_exchangeName} publish", ActivityKind.Producer);
        _metrics.RecordPublish(_exchangeName);
        try
        {
            var channel = await _channel;
            var buffer = new ArrayBufferWriter<byte>();
            _serializer.Serialize(message, buffer);
            var props = new BasicProperties
            {
                MessageId = Guid.NewGuid().ToString(),
                Headers = new Dictionary<string, object?> { ["traceparent"] = activity?.Id },
            };
            await channel.BasicPublishAsync(_exchangeName, routingKey: "", mandatory: false,
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
    }

    public async Task PublishBatchAsync(IEnumerable<T> messages, CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();
        using var activity = MessageBrokerActivitySource.Source.StartActivity(
            $"{_exchangeName} publish", ActivityKind.Producer);
        _metrics.RecordPublish(_exchangeName, messageList.Count);
        try
        {
            var channel = await _channel;
            var traceId = activity?.Id;
            foreach (var message in messageList)
            {
                var buffer = new ArrayBufferWriter<byte>();
                _serializer.Serialize(message, buffer);
                await channel.BasicPublishAsync(_exchangeName, routingKey: "", mandatory: false,
                    new BasicProperties
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        Headers = new Dictionary<string, object?> { ["traceparent"] = traceId },
                    },
                    buffer.WrittenMemory, cancellationToken);
            }
        }
        catch (BrokerUnreachableException ex)
        {
            throw new BrokerUnavailableException(_exchangeName, ex);
        }
        catch (OperationInterruptedException ex)
        {
            throw new BrokerUnavailableException(_exchangeName, ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel.IsCompletedSuccessfully)
            await _channel.Result.DisposeAsync();
    }
}
