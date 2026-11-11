using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class RabbitSubscriber<TFamily, TCeiling> : ISubscriber<TFamily, TCeiling>
    where TFamily : FamilyCeiling<TFamily, TCeiling>, IFamilyVariants<TFamily>, IFamilyWireName<TFamily>
    where TCeiling : Family<TFamily>.ICeiling
{
    private readonly string _exchangeName;
    private readonly string _queueName;
    private readonly RabbitConnection _connection;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;

    public RabbitSubscriber(RabbitConnection connection, string exchangeName, string subscriptionName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _exchangeName = exchangeName;
        _queueName = $"{exchangeName}.{subscriptionName}";
        _connection = connection;
        _serializer = serializer;
        _metrics = metrics;
    }

    public async IAsyncEnumerable<Envelope<TFamily, TCeiling>> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conn = await _connection.GetConnectionAsync(cancellationToken);
        await using var rabbitChannel = await conn.CreateChannelAsync(cancellationToken: cancellationToken);

        var innerChannel = Channel.CreateUnbounded<RabbitEnvelope>();

        var consumer = new AsyncEventingBasicConsumer(rabbitChannel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            // Defense in depth: when operator binding routes correctly, only matching messages
            // arrive. A misconfigured binding (wrong routing key, or no header filter) could
            // deliver another family's message here, and decoding it against TFamily would
            // silently discard every variant and dead-letter on the length check below with
            // no indication the real problem was routing. Fail fast with the actual reason.
            if (ea.BasicProperties.Headers?.TryGetValue(BrokerProperties.Family, out var familyBytes) == true
                && familyBytes is byte[] familyBytesArr
                && Encoding.UTF8.GetString(familyBytesArr) is var familyName
                && familyName != TFamily.WireName)
            {
                await rabbitChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            IReadOnlyList<Family<TFamily>.IVariant> variants;
            try
            {
                variants = _serializer.DeserializeVariants<TFamily>(ea.Body.Span);
            }
            catch (Exception)
            {
                await rabbitChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            if (variants.Count == 0)
            {
                // Every variant on the wire was unknown to this subscriber. Dead-letter so the
                // broker does not retry into an unrecoverable state.
                await rabbitChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            var messageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString();
            var traceId = ea.BasicProperties.Headers?.TryGetValue(BrokerProperties.TraceParent, out var tp) == true && tp is byte[] tpBytes
                ? Encoding.UTF8.GetString(tpBytes)
                : null;
            // Quorum queues (Rabbit 3.12+) set x-delivery-count starting at 0; add 1 to
            // match the 1-based DeliveryCount convention (first delivery = 1).
            // Classic queues only expose a boolean redelivered flag, so we fall back to 1/2.
            var deliveryCount = ea.BasicProperties.Headers?.TryGetValue("x-delivery-count", out var dc) == true && dc is long count
                ? (int)count + 1
                : ea.Redelivered ? 2 : 1;
            var activity = MessageBrokerActivitySource.StartConsumerActivity(_exchangeName, TFamily.WireName, traceId);

            RabbitEnvelope envelope;
            try
            {
                envelope = new RabbitEnvelope(variants, ea.DeliveryTag, rabbitChannel, messageId, traceId, deliveryCount, activity);
            }
            catch (InvalidOperationException)
            {
                // Received variants cannot be resolved to TCeiling — dead-letter.
                activity?.Dispose();
                await rabbitChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await innerChannel.Writer.WriteAsync(envelope, CancellationToken.None);
        };

        // When the broker closes the channel unexpectedly, record the reason and complete the
        // writer so ReadAllAsync returns naturally. We complete without propagating the exception
        // so that yield return remains valid (CS1626 forbids yield in a try-catch body).
        // The stored exception is checked after the loop and surfaced as BrokerDisconnectedException.
        Exception? brokerDisconnect = null;
        rabbitChannel.ChannelShutdownAsync += (_, args) =>
        {
            if (args.Initiator != ShutdownInitiator.Application)
                brokerDisconnect = new Exception(args.ReplyText);
            innerChannel.Writer.TryComplete();
            return Task.CompletedTask;
        };

        // Limit unacked messages per consumer so the broker distributes messages fairly
        // across competing consumers rather than pushing everything to one consumer.
        await rabbitChannel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: cancellationToken);
        await rabbitChannel.BasicConsumeAsync(_queueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);

        await foreach (var envelope in innerChannel.Reader.ReadAllAsync(cancellationToken))
        {
            _metrics.RecordConsume(_exchangeName, TFamily.WireName, envelope.ConsumedVariantWireName);
            yield return envelope;
        }

        if (brokerDisconnect is not null)
            throw new BrokerDisconnectedException(_exchangeName, brokerDisconnect);
    }

    private sealed class RabbitEnvelope : Envelope<TFamily, TCeiling>
    {
        private readonly ulong _deliveryTag;
        private readonly IChannel _channel;

        public RabbitEnvelope(
            IReadOnlyList<Family<TFamily>.IVariant> variants,
            ulong deliveryTag,
            IChannel channel,
            string messageId,
            string? traceId,
            int deliveryCount,
            Activity? activity)
            : base(variants, activity)
        {
            _deliveryTag = deliveryTag;
            _channel = channel;
            MessageId = messageId;
            TraceId = traceId;
            DeliveryCount = deliveryCount;
        }

        public override string MessageId { get; }
        public override string? TraceId { get; }
        public override int DeliveryCount { get; }

        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) =>
            _channel.BasicAckAsync(_deliveryTag, multiple: false, cancellationToken).AsTask();

        // If the channel is already closed (iterator was disposed), Rabbit automatically
        // requeued the unacked message on close, so there is nothing left to do.
        protected override Task RequeueCoreAsync(CancellationToken cancellationToken) =>
            _channel.IsOpen ? _channel.BasicNackAsync(_deliveryTag, multiple: false, requeue: true, cancellationToken).AsTask() : Task.CompletedTask;

        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) =>
            _channel.IsOpen ? _channel.BasicNackAsync(_deliveryTag, multiple: false, requeue: false, cancellationToken).AsTask() : Task.CompletedTask;
    }
}
