using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Keeps a received message's lock from expiring while its handler runs and while the consumer
/// waits to retry it, until <see cref="StopAsync"/> or <see cref="MessagingOptions.MaxLockRenewalDuration"/>.
/// </summary>
internal sealed class AzureServiceBusLockRenewal
{
    private static readonly TimeSpan MinimumWait = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stop = new();
    private readonly Task _renewing;

    public AzureServiceBusLockRenewal(ServiceBusReceiver receiver, ServiceBusReceivedMessage message, TimeSpan maxDuration)
    {
        _renewing = RenewAsync(receiver, message, DateTimeOffset.UtcNow + maxDuration, _stop.Token);
    }

    /// <summary>
    /// Stops renewing and waits for a renewal under way, so it never overlaps settlement. Call before
    /// settling the message.
    /// </summary>
    public async Task StopAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync();
        }

        await _renewing;
        _stop.Dispose();
    }

    private static async Task RenewAsync(
        ServiceBusReceiver receiver, ServiceBusReceivedMessage message, DateTimeOffset renewUntil, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var wait = (message.LockedUntil - DateTimeOffset.UtcNow) / 2;
                if (wait < MinimumWait)
                {
                    wait = MinimumWait;
                }

                if (DateTimeOffset.UtcNow + wait > renewUntil)
                {
                    return;
                }

                await Task.Delay(wait, stop);
                if (receiver.IsClosed)
                {
                    return;
                }
                // Updates message.LockedUntil, which the next wait reads.
                await receiver.RenewMessageLockAsync(message, stop);
            }
        }
        catch (Exception)
        {
            // Stopped, the receiver closed, or the lock was lost and the broker will redeliver.
        }
    }
}
