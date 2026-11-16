using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Receives every message on an Azure Service Bus queue.</summary>
internal sealed class AzureServiceBusCommandQueueReceiver : ICommandQueueReceiver
{
    private readonly AzureServiceBusConnection _connection;
    private readonly string _queueName;
    private readonly TimeSpan _maxLockRenewal;

    public AzureServiceBusCommandQueueReceiver(AzureServiceBusConnection connection, string queueName, TimeSpan maxLockRenewal)
    {
        _maxLockRenewal = maxLockRenewal;
        _connection = connection;
        _queueName = queueName;
    }

    public async IAsyncEnumerable<CommandQueueMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ServiceBusReceiver receiver;
        try
        {
            receiver = _connection.Client
                .CreateReceiver(_queueName, new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        }
        catch (ObjectDisposedException) when (_connection.IsClosing)
        {
            // The connection closed before the stream started.
            yield break;
        }

        await using var receiverLifetime = receiver;

        // Stop waiting for a receive once either the caller cancels (the host stopping this
        // service, which comes before the connection closes) or the connection closes.
        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connection.Closed);

        while (!cancellationToken.IsCancellationRequested && !_connection.IsClosing)
        {
            // One message at a time, as AzureServiceBusSubscriber does, so stopping leaves at most
            // one message locked by this receiver.
            IReadOnlyList<ServiceBusReceivedMessage> received;
            try
            {
                // As AzureServiceBusSubscriber does: the Azure SDK neither cancels a receive still
                // opening its link nor ends it when the client closes, so stop waiting for it, and
                // observe the failure it ends with.
                var receive = receiver.ReceiveMessagesAsync(maxMessages: 1, cancellationToken: cancellationToken);
                _ = receive.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                received = await receive.WaitAsync(stopWaiting.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or ServiceBusException
                                       && _connection.IsClosing
                                       && !cancellationToken.IsCancellationRequested)
            {
                // The connection closed while the receive was opening its link or waiting for a
                // message: a shutdown, whatever the Azure SDK surfaced. End the stream.
                break;
            }
            catch (ServiceBusException ex)
            {
                throw new BrokerDisconnectedException(_queueName, ex);
            }

            foreach (var sbMessage in received)
            {
                var lockRenewal = new AzureServiceBusLockRenewal(receiver, sbMessage, _maxLockRenewal);
                yield return new CommandQueueMessage
                {
                    Body = sbMessage.Body.ToMemory(),
                    FamilyWireName = sbMessage.ApplicationProperties.TryGetValue(BrokerProperties.PayloadFamily, out var family)
                        ? family as string
                        : null,
                    MessageId = sbMessage.MessageId,
                    TraceParent = sbMessage.ApplicationProperties.TryGetValue(BrokerProperties.TraceParent, out var tp)
                        ? tp as string
                        : null,
                    DeliveryCount = sbMessage.DeliveryCount,
                    CompleteAsync = ct =>
                    {
                        lockRenewal.Stop();
                        return receiver.CompleteMessageAsync(sbMessage, ct);
                    },
                    // A closed receiver released its locks, which is the same as abandoning.
                    RequeueAsync = ct =>
                    {
                        lockRenewal.Stop();
                        return receiver.IsClosed ? Task.CompletedTask : receiver.AbandonMessageAsync(sbMessage, cancellationToken: ct);
                    },
                    DeadLetterAsync = (reason, ct) =>
                    {
                        lockRenewal.Stop();
                        return receiver.IsClosed
                            ? Task.CompletedTask
                            : receiver.DeadLetterMessageAsync(sbMessage, deadLetterReason: reason, cancellationToken: ct);
                    },
                };
            }
        }
    }
}
