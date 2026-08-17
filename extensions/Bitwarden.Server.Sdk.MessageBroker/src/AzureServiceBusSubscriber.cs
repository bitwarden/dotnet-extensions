using System.Diagnostics;
using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

internal sealed class AzureServiceBusSubscriber<T> : ISubscriber<T>, IAsyncDisposable
{
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private readonly ServiceBusClient _client;
    private readonly IMessageSerializer _serializer;
    private readonly MessageBrokerMetrics _metrics;

    public AzureServiceBusSubscriber(string connectionString, string topicName, string subscriptionName, IMessageSerializer serializer, MessageBrokerMetrics metrics)
    {
        _topicName = topicName;
        _subscriptionName = subscriptionName;
        _client = new ServiceBusClient(connectionString);
        _serializer = serializer;
        _metrics = metrics;
    }

    public async IAsyncEnumerable<Envelope<T>> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var receiver = _client
            .CreateReceiver(_topicName, _subscriptionName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });

        while (!cancellationToken.IsCancellationRequested && !_client.IsClosed)
        {
            // Receive one message at a time so the receiver holds at most one pending lock.
            // This ensures that when iteration stops (break / cancellation), no unprocessed
            // messages are left locked by this receiver.
            IReadOnlyList<ServiceBusReceivedMessage> received;
            try
            {
                received = await receiver.ReceiveMessagesAsync(maxMessages: 1, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ObjectDisposedException) when (_client.IsClosed)
            {
                // The client was disposed while we were waiting — exit the loop cleanly.
                break;
            }
            catch (ServiceBusException ex)
            {
                throw new BrokerDisconnectedException(_topicName, ex);
            }

            foreach (var sbMessage in received)
            {
                T? message;
                try
                {
                    message = _serializer.Deserialize<T>(sbMessage.Body.ToArray());
                }
                catch (Exception)
                {
                    await receiver.DeadLetterMessageAsync(sbMessage, cancellationToken: cancellationToken);
                    continue;
                }

                if (message is not null)
                {
                    _metrics.RecordConsume(_topicName);
                    var traceId = sbMessage.ApplicationProperties.TryGetValue("traceparent", out var tp) ? tp as string : null;
                    var activity = MessageBrokerActivitySource.StartConsumerActivity(_topicName, traceId);
                    yield return new AzureServiceBusEnvelope(message, receiver, sbMessage, activity);
                }
                else
                {
                    await receiver.DeadLetterMessageAsync(sbMessage, cancellationToken: cancellationToken);
                }
            }
        }
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private sealed class AzureServiceBusEnvelope : Envelope<T>
    {
        private readonly ServiceBusReceiver _receiver;
        private readonly ServiceBusReceivedMessage _sbMessage;

        public AzureServiceBusEnvelope(T message, ServiceBusReceiver receiver, ServiceBusReceivedMessage sbMessage, Activity? activity)
            : base(message, activity)
        {
            _receiver = receiver;
            _sbMessage = sbMessage;
        }

        public override string MessageId => _sbMessage.MessageId;
        public override string? TraceId => _sbMessage.ApplicationProperties.TryGetValue("traceparent", out var tp) ? tp as string : null;
        public override int DeliveryCount => _sbMessage.DeliveryCount;

        protected override Task CompleteAsyncCore(CancellationToken cancellationToken) =>
            _receiver.CompleteMessageAsync(_sbMessage, cancellationToken);

        // If the receiver is already closed (iterator was disposed), the lock was released
        // automatically on close, which is equivalent to abandonment.
        protected override Task AbandonCoreAsync(CancellationToken cancellationToken) =>
            _receiver.IsClosed ? Task.CompletedTask : _receiver.AbandonMessageAsync(_sbMessage, cancellationToken: cancellationToken);

        protected override Task DeadLetterAsyncCore(string? reason, CancellationToken cancellationToken) =>
            _receiver.IsClosed ? Task.CompletedTask : _receiver.DeadLetterMessageAsync(_sbMessage, deadLetterReason: reason, cancellationToken: cancellationToken);
    }
}
