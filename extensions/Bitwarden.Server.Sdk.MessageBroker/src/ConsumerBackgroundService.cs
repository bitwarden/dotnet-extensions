using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Hosts an <see cref="IMessageConsumer{T}"/> as a <see cref="BackgroundService"/>, driving its
/// message-processing loop and settling each envelope automatically.
/// </summary>
internal sealed class ConsumerBackgroundService<T, TConsumer> : BackgroundService
    where TConsumer : class, IMessageConsumer<T>
{
    private readonly TConsumer _consumer;
    private readonly ISubscriber<T> _subscriber;

    /// <param name="consumer">The consumer to invoke for each message.</param>
    /// <param name="subscriber">The subscriber to consume messages from.</param>
    public ConsumerBackgroundService(TConsumer consumer, ISubscriber<T> subscriber)
    {
        _consumer = consumer;
        _subscriber = subscriber;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in _subscriber.SubscribeAsync(stoppingToken))
        {
            // Make the consumer span current so telemetry emitted inside HandleAsync (nested
            // activities, HttpClient spans, EF Core command spans, scoped logs) is parented to
            // the receive span. The backends create the Activity on the subscriber iterator's
            // execution context, which the async enumerator does not flow out to this body.
            var previousActivity = Activity.Current;
            Activity.Current = envelope.Activity;
            try
            {
                await _consumer.HandleAsync(envelope, stoppingToken);
                // Settle with CancellationToken.None: passing along a stopped token after a
                // successful handler would leave the message un-acked and unsettled.
                await envelope.CompleteAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                try
                {
                    await envelope.RequeueAsync(ex.Message, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Settlement failed (broker disconnected, lock expired, etc.). The broker
                    // will redeliver the message when the lock times out. Swallow so a transient
                    // settlement error doesn't kill the consumer permanently.
                }
            }
            finally
            {
                Activity.Current = previousActivity;
            }
        }
    }
}
