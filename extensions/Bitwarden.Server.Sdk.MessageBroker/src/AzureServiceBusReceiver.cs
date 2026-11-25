using System.Runtime.CompilerServices;
using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Receives every message on one Azure Service Bus queue or topic subscription.
/// </summary>
internal sealed class AzureServiceBusReceiver : IMessageReceiver
{
    private readonly AzureServiceBusConnection _connection;
    private readonly string _entityName;
    private readonly string? _subscriptionName;
    private readonly TimeSpan _maxLockRenewal;

    /// <param name="connection">The shared Azure Service Bus connection.</param>
    /// <param name="entityName">The command queue, or the topic when <paramref name="subscriptionName"/> is set.</param>
    /// <param name="subscriptionName">The topic subscription to receive from, or null for a queue.</param>
    /// <param name="maxLockRenewal">How long to keep renewing a received message's lock while it is handled.</param>
    public AzureServiceBusReceiver(AzureServiceBusConnection connection, string entityName, string? subscriptionName, TimeSpan maxLockRenewal)
    {
        _maxLockRenewal = maxLockRenewal;
        _connection = connection;
        _entityName = entityName;
        _subscriptionName = subscriptionName;
    }

    public async IAsyncEnumerable<ReceivedMessage> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ServiceBusReceiver receiver;
        try
        {
            var options = new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock };
            receiver = _subscriptionName is null
                ? _connection.Client.CreateReceiver(_entityName, options)
                : _connection.Client.CreateReceiver(_entityName, _subscriptionName, options);
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
                throw new BrokerDisconnectedException(_entityName, ex);
            }

            foreach (var sbMessage in received)
            {
                var lockRenewal = new AzureServiceBusLockRenewal(receiver, sbMessage, _maxLockRenewal);
                yield return new ReceivedMessage
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
                    CompleteAsync = async ct =>
                    {
                        await lockRenewal.StopAsync();
                        await receiver.CompleteMessageAsync(sbMessage, ct);
                    },
                    // A closed receiver released its locks, which is the same as abandoning.
                    RequeueAsync = async ct =>
                    {
                        await lockRenewal.StopAsync();
                        if (!receiver.IsClosed)
                        {
                            await receiver.AbandonMessageAsync(sbMessage, cancellationToken: ct);
                        }
                    },
                    DeadLetterAsync = async (reason, ct) =>
                    {
                        await lockRenewal.StopAsync();
                        if (!receiver.IsClosed)
                        {
                            await receiver.DeadLetterMessageAsync(sbMessage, deadLetterReason: reason, cancellationToken: ct);
                        }
                    },
                };
            }
        }
    }
}
