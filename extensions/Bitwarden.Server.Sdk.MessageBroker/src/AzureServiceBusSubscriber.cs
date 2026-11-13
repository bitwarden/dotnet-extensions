using System.Diagnostics;
using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class AzureServiceBusSubscriber<TPayload, TCeiling> : ISubscriber<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private readonly AzureServiceBusConnection _connection;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;

    public AzureServiceBusSubscriber(AzureServiceBusConnection connection, string topicName, string subscriptionName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _topicName = topicName;
        _subscriptionName = subscriptionName;
        _connection = connection;
        _serializer = serializer;
        _metrics = metrics;
    }

    public async IAsyncEnumerable<Envelope<TPayload, TCeiling>> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ServiceBusReceiver receiver;
        try
        {
            receiver = _connection.Client
                .CreateReceiver(_topicName, _subscriptionName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        }
        catch (ObjectDisposedException) when (_connection.IsClosing)
        {
            // The connection closed before the stream started.
            yield break;
        }

        await using var receiverLifetime = receiver;

        while (!cancellationToken.IsCancellationRequested && !_connection.IsClosing)
        {
            // Receive one message at a time so the receiver holds at most one pending lock.
            // This ensures that when iteration stops (break / cancellation), no unprocessed
            // messages are left locked by this receiver.
            IReadOnlyList<ServiceBusReceivedMessage> received;
            try
            {
                // The Azure SDK does not cancel a receive that is still opening its link, and closing
                // the client does not end it either: it waits out its try timeout. Stop waiting for it
                // once the connection has closed, and observe the failure it ends with.
                var receive = receiver.ReceiveMessagesAsync(maxMessages: 1, cancellationToken: cancellationToken);
                _ = receive.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                received = await receive.WaitAsync(_connection.Closed);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or ServiceBusException
                                       && _connection.IsClosing
                                       && !cancellationToken.IsCancellationRequested)
            {
                // The connection closed while the receive was opening its link or waiting for a
                // message. Whatever the Azure SDK surfaced, this is a shutdown: end the stream.
                break;
            }
            catch (ServiceBusException ex)
            {
                throw new BrokerDisconnectedException(_topicName, ex);
            }

            foreach (var sbMessage in received)
            {
                // Defense in depth: when the operator-declared Correlation filter routes correctly,
                // only matching messages arrive. A missing or wrong filter could deliver another
                // family's message here; decoding it against TPayload would silently discard every
                // variant and dead-letter on the length check below with no indication the real
                // problem was the subscription filter. Fail fast with the actual reason.
                if (sbMessage.ApplicationProperties.TryGetValue(BrokerProperties.PayloadFamily, out var familyObj)
                    && familyObj is string familyName
                    && familyName != TPayload.WireName)
                {
                    await receiver.DeadLetterMessageAsync(sbMessage, cancellationToken: cancellationToken);
                    continue;
                }

                IReadOnlyList<Payload<TPayload>.IVariant> variants;
                try
                {
                    variants = _serializer.DeserializeVariants<TPayload>(sbMessage.Body.ToArray());
                }
                catch (Exception)
                {
                    await receiver.DeadLetterMessageAsync(sbMessage, cancellationToken: cancellationToken);
                    continue;
                }

                if (variants.Count == 0)
                {
                    // No variant on the wire was known to this subscriber.
                    await receiver.DeadLetterMessageAsync(sbMessage, cancellationToken: cancellationToken);
                    continue;
                }

                var traceId = sbMessage.ApplicationProperties.TryGetValue(BrokerProperties.TraceParent, out var tp) ? tp as string : null;
                var activity = MessageBrokerActivitySource.StartConsumerActivity(_topicName, TPayload.WireName, traceId);

                AzureServiceBusEnvelope envelope;
                try
                {
                    envelope = new AzureServiceBusEnvelope(variants, receiver, sbMessage, activity);
                }
                catch (InvalidOperationException)
                {
                    // Received variants cannot be resolved to TCeiling — dead-letter.
                    activity?.Dispose();
                    await receiver.DeadLetterMessageAsync(sbMessage, cancellationToken: cancellationToken);
                    continue;
                }

                _metrics.RecordConsume(_topicName, TPayload.WireName, envelope.ConsumedVariantWireName);
                yield return envelope;
            }
        }
    }

    private sealed class AzureServiceBusEnvelope : Envelope<TPayload, TCeiling>
    {
        private readonly ServiceBusReceiver _receiver;
        private readonly ServiceBusReceivedMessage _sbMessage;

        public AzureServiceBusEnvelope(
            IReadOnlyList<Payload<TPayload>.IVariant> variants,
            ServiceBusReceiver receiver,
            ServiceBusReceivedMessage sbMessage,
            Activity? activity)
            : base(variants, activity)
        {
            _receiver = receiver;
            _sbMessage = sbMessage;
        }

        public override string MessageId => _sbMessage.MessageId;
        public override string? TraceId => _sbMessage.ApplicationProperties.TryGetValue(BrokerProperties.TraceParent, out var tp) ? tp as string : null;
        public override int DeliveryCount => _sbMessage.DeliveryCount;

        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) =>
            _receiver.CompleteMessageAsync(_sbMessage, cancellationToken);

        // If the receiver is already closed (iterator was disposed), the lock was released
        // automatically on close, which is equivalent to abandonment.
        protected override Task RequeueCoreAsync(CancellationToken cancellationToken) =>
            _receiver.IsClosed ? Task.CompletedTask : _receiver.AbandonMessageAsync(_sbMessage, cancellationToken: cancellationToken);

        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) =>
            _receiver.IsClosed ? Task.CompletedTask : _receiver.DeadLetterMessageAsync(_sbMessage, deadLetterReason: reason, cancellationToken: cancellationToken);
    }
}
