using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// RabbitMQ implementation of <see cref="INegotiationTransport"/>. Declares on first use:
/// a direct exchange named <see cref="NegotiationOptions.ControlTopicName"/>, and a classic queue
/// <see cref="NegotiationOptions.RequestSubscriptionName"/> with
/// <c>x-single-active-consumer=true</c> bound to each entry in <c>_topicsToBind</c>.
/// Replies flow through the per-connection <c>amq.rabbitmq.reply-to</c> pseudo-queue.
/// <para>
/// Construct via <see cref="ForListener"/>, <see cref="ForPublisherSender"/>, or
/// <see cref="ForSubscriberSender"/> — the factory name pins the role and prevents the
/// send-only vs listener choice from being expressed as an argument value at the call site.
/// </para>
/// </summary>
internal sealed class RabbitNegotiationTransport : INegotiationTransport, IAsyncDisposable
{
    private const string DirectReplyToQueueName = "amq.rabbitmq.reply-to";
    private const string CapabilityType = "capability";
    private const string JoinType = "join";

    private readonly NegotiationOptions _options;
    private readonly RabbitConnection _connection;
    private readonly IReadOnlyCollection<string> _topicsToBind;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IChannel? _channel;

    // Outstanding requests awaiting a reply, keyed by the message id we set on the outgoing
    // request. The server echoes it back as CorrelationId on the reply.
    private readonly ConcurrentDictionary<string, TaskCompletionSource<NegotiationAck>> _pendingReplies = new();

    private RabbitNegotiationTransport(
        RabbitConnection connection,
        IOptions<NegotiationOptions> negotiationOptions,
        IReadOnlyCollection<string> topicsToBind)
    {
        _connection = connection;
        _options = negotiationOptions.Value;
        _topicsToBind = topicsToBind;
    }

    /// <summary>
    /// Listener role. Binds the request queue for every topic this service serves so incoming
    /// negotiation requests route to us, and consumes them via
    /// <see cref="ReceiveRequestsAsync"/>.
    /// </summary>
    public static RabbitNegotiationTransport ForListener(
        RabbitConnection connection,
        IOptions<NegotiationOptions> negotiationOptions,
        IReadOnlyCollection<string> topics)
        => new(connection, negotiationOptions, topics);

    /// <summary>
    /// Publisher send side. Pre-declares queue bindings for the given topics so a race between
    /// the requester's publish and the listener's fire-and-forget setup can't leave the
    /// message unrouted (Rabbit publishes are <c>mandatory: false</c>, so an unrouted message
    /// is silently dropped and the requester times out on the reply). <c>QueueBind</c> is
    /// idempotent, so declaring it from both sides is safe.
    /// </summary>
    public static RabbitNegotiationTransport ForPublisherSender(
        RabbitConnection connection,
        IOptions<NegotiationOptions> negotiationOptions,
        IReadOnlyCollection<string> topicsToPreBind)
        => new(connection, negotiationOptions, topicsToPreBind);

    /// <summary>
    /// Subscriber send side. Declares no bindings — subscribers don't own the request queue,
    /// and replies flow through the connection-scoped <c>amq.rabbitmq.reply-to</c>
    /// pseudo-queue.
    /// </summary>
    public static RabbitNegotiationTransport ForSubscriberSender(
        RabbitConnection connection,
        IOptions<NegotiationOptions> negotiationOptions)
        => new(connection, negotiationOptions, []);

    public Task<NegotiationAck> SendCapabilityAsync(Capability capability, CancellationToken cancellationToken = default)
        => SendAndAwaitAsync(
            capability.DataTopic,
            JsonSerializer.SerializeToUtf8Bytes(capability, NegotiationJsonContext.Default.Capability),
            CapabilityType,
            cancellationToken);

    public Task<NegotiationAck> SendJoinAsync(PublisherJoin join, CancellationToken cancellationToken = default)
        => SendAndAwaitAsync(
            join.DataTopic,
            JsonSerializer.SerializeToUtf8Bytes(join, NegotiationJsonContext.Default.PublisherJoin),
            JoinType,
            cancellationToken);

    private async Task<NegotiationAck> SendAndAwaitAsync(
        string dataTopic,
        byte[] body,
        string type,
        CancellationToken cancellationToken)
    {
        var channel = await EnsureAsync(cancellationToken);

        var messageId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<NegotiationAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingReplies[messageId] = tcs;

        try
        {
            var props = new BasicProperties
            {
                MessageId = messageId,
                CorrelationId = messageId,
                Type = type,
                ReplyTo = DirectReplyToQueueName,
            };
            await channel.BasicPublishAsync(
                _options.ControlTopicName,
                routingKey: dataTopic,
                mandatory: false,
                props,
                body,
                cancellationToken);

            using var timeoutCts = new CancellationTokenSource(_options.AdmissionTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            return await tcs.Task.WaitAsync(linkedCts.Token);
        }
        finally
        {
            _pendingReplies.TryRemove(messageId, out _);
        }
    }

    public async IAsyncEnumerable<INegotiationRequest> ReceiveRequestsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = await EnsureAsync(cancellationToken);

        var inbox = System.Threading.Channels.Channel.CreateUnbounded<(INegotiationRequest Request, ulong DeliveryTag)>();

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            var request = BuildRequest(ea, channel);
            if (request is null)
            {
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }
            await inbox.Writer.WriteAsync((request, ea.DeliveryTag), CancellationToken.None);
        };

        // Limit outstanding un-acked messages so SAC failover from another instance is not
        // starved of work by prefetch on this one. global: false scopes this to the consumer
        // being registered below, leaving the reply consumer's flow untouched.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: cancellationToken);
        var consumerTag = await channel.BasicConsumeAsync(
            _options.RequestSubscriptionName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        try
        {
            await foreach (var (request, deliveryTag) in inbox.Reader.ReadAllAsync(cancellationToken))
            {
                yield return request;
                await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
            }
        }
        finally
        {
            inbox.Writer.TryComplete();
            if (channel.IsOpen)
            {
                // The caller's token is often already cancelled here (that is how we exited the
                // read loop), so pass None to let cleanup run.
                try { await channel.BasicCancelAsync(consumerTag, cancellationToken: CancellationToken.None); }
                catch { /* channel already closing */ }
            }
        }
    }

    private async Task<IChannel> EnsureAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) return _channel;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null) return _channel;

            // Share the process-wide connection managed by RabbitConnection so this transport
            // does not open a second connection to the same broker for the same process.
            var connection = await _connection.GetConnectionAsync(cancellationToken);
            IChannel? channel = null;
            try
            {
                channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

                await channel.ExchangeDeclareAsync(
                    _options.ControlTopicName,
                    ExchangeType.Direct,
                    durable: true,
                    cancellationToken: cancellationToken);
                await channel.QueueDeclareAsync(
                    _options.RequestSubscriptionName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: new Dictionary<string, object?> { ["x-single-active-consumer"] = true },
                    cancellationToken: cancellationToken);
                foreach (var dataTopic in _topicsToBind)
                {
                    await channel.QueueBindAsync(
                        _options.RequestSubscriptionName,
                        _options.ControlTopicName,
                        routingKey: dataTopic,
                        cancellationToken: cancellationToken);
                }

                // Direct reply-to consumer. autoAck is required by the pseudo-queue. Registered
                // eagerly so a reply cannot race the consumer setup on the first SendAndAwait.
                var replyConsumer = new AsyncEventingBasicConsumer(channel);
                replyConsumer.ReceivedAsync += OnReplyReceived;
                await channel.BasicConsumeAsync(
                    DirectReplyToQueueName,
                    autoAck: true,
                    consumer: replyConsumer,
                    cancellationToken: cancellationToken);

                _channel = channel;
                return _channel;
            }
            catch
            {
                if (channel is not null) await channel.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private Task OnReplyReceived(object sender, BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.CorrelationId is { } correlationId
            && _pendingReplies.TryGetValue(correlationId, out var tcs))
        {
            try
            {
                var ack = JsonSerializer.Deserialize(ea.Body.Span, NegotiationJsonContext.Default.NegotiationAck);
                if (ack is not null) tcs.TrySetResult(ack);
            }
            catch (JsonException)
            {
                // Drop; the pending send will time out.
            }
        }
        return Task.CompletedTask;
    }

    private INegotiationRequest? BuildRequest(BasicDeliverEventArgs ea, IChannel channel)
    {
        // Copy properties needed by the replier before returning from the consumer callback so
        // the reply closure does not capture the delivery event args past their lifetime.
        var replyTo = ea.BasicProperties.ReplyTo;
        var correlationId = ea.BasicProperties.CorrelationId ?? ea.BasicProperties.MessageId;
        Task Replier(NegotiationAck ack, CancellationToken ct) =>
            SendReplyAsync(channel, replyTo, correlationId, ack, ct);

        try
        {
            return ea.BasicProperties.Type switch
            {
                CapabilityType => new CapabilityRequest
                {
                    Capability = JsonSerializer.Deserialize(ea.Body.Span, NegotiationJsonContext.Default.Capability)
                        ?? throw new JsonException("null capability body"),
                    Replier = Replier,
                },
                JoinType => new JoinRequest
                {
                    Join = JsonSerializer.Deserialize(ea.Body.Span, NegotiationJsonContext.Default.PublisherJoin)
                        ?? throw new JsonException("null join body"),
                    Replier = Replier,
                },
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task SendReplyAsync(
        IChannel channel,
        string? replyTo,
        string? correlationId,
        NegotiationAck ack,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(replyTo))
            return Task.CompletedTask;

        var replyProps = new BasicProperties { CorrelationId = correlationId };
        var body = JsonSerializer.SerializeToUtf8Bytes(ack, NegotiationJsonContext.Default.NegotiationAck);
        // Default exchange with routing key = the requester's direct-reply pseudo-queue name.
        return channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: replyTo,
            mandatory: false,
            replyProps,
            body,
            cancellationToken).AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _initLock.Dispose();
    }
}
