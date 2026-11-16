using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Keeps a received message's lock from expiring while its handler runs, and while the consumer
/// waits before redelivering it after a failure. Without renewal a handler that outlasts the
/// lock duration loses the message: the broker hands it to another receiver while this one is
/// still working on it, and this one's settlement then fails. The lock is renewed when half of
/// it remains, until <see cref="Stop"/> is called at settlement or the renewal has run for
/// <see cref="MessagingOptions.MaxLockRenewalDuration"/>. The bound keeps the broker's backstop
/// for a handler that hangs: its lock then expires and the message is delivered again.
/// </summary>
internal sealed class AzureServiceBusLockRenewal
{
    private static readonly TimeSpan MinimumWait = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stop = new();

    public AzureServiceBusLockRenewal(ServiceBusReceiver receiver, ServiceBusReceivedMessage message, TimeSpan maxDuration)
    {
        _ = RenewAsync(receiver, message, DateTimeOffset.UtcNow + maxDuration, _stop.Token);
    }

    /// <summary>Stops renewing. Call before settling the message.</summary>
    public void Stop() => _stop.Cancel();

    private static async Task RenewAsync(
        ServiceBusReceiver receiver, ServiceBusReceivedMessage message, DateTimeOffset renewUntil, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var wait = (message.LockedUntil - DateTimeOffset.UtcNow) / 2;
                if (wait < MinimumWait)
                    wait = MinimumWait;
                if (DateTimeOffset.UtcNow + wait > renewUntil)
                    return;
                await Task.Delay(wait, stop);
                if (receiver.IsClosed)
                    return;
                // Updates message.LockedUntil, which the next wait reads.
                await receiver.RenewMessageLockAsync(message, stop);
            }
        }
        catch (Exception)
        {
            // Stopped at settlement, or the receiver closed, or the lock was already lost. In the
            // last case settlement fails and the broker redelivers the message, as it would have
            // without renewal.
        }
    }
}
