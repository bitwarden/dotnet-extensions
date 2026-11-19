using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Receives every message on an Azure Service Bus queue.</summary>
internal sealed class AzureServiceBusCommandQueueReceiver : ICommandQueueReceiver
{
    private readonly AzureServiceBusConnection _connection;
    private readonly string _queueName;

    public AzureServiceBusCommandQueueReceiver(AzureServiceBusConnection connection, string queueName)
    {
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
            yield break;
        }

        await using var receiverLifetime = receiver;

        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connection.Closed);

        while (!cancellationToken.IsCancellationRequested && !_connection.IsClosing)
        {
            // One message at a time, so stopping leaves at most one locked.
            IReadOnlyList<ServiceBusReceivedMessage> received;
            try
            {
                // The Azure SDK doesn't cancel a receive still opening its link: stop waiting, observe its failure.
                var receive = receiver.ReceiveMessagesAsync(maxMessages: 1, cancellationToken: cancellationToken);
                _ = receive.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                received = await receive.WaitAsync(stopWaiting.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or ServiceBusException
                                       && _connection.IsClosing
                                       && !cancellationToken.IsCancellationRequested)
            {
                // A shutdown, whatever the Azure SDK surfaced.
                break;
            }
            catch (ServiceBusException ex)
            {
                throw new BrokerDisconnectedException(_queueName, ex);
            }

            foreach (var sbMessage in received)
            {
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
                    CompleteAsync = ct => receiver.CompleteMessageAsync(sbMessage, ct),
                    // A closed receiver released its locks, which is the same as abandoning.
                    RequeueAsync = ct => receiver.IsClosed
                        ? Task.CompletedTask
                        : receiver.AbandonMessageAsync(sbMessage, cancellationToken: ct),
                    DeadLetterAsync = (reason, ct) => receiver.IsClosed
                        ? Task.CompletedTask
                        : receiver.DeadLetterMessageAsync(sbMessage, deadLetterReason: reason, cancellationToken: ct),
                };
            }
        }
    }
}
