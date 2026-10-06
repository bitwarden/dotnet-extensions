using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Hosts an <see cref="IMessageConsumer{T}"/> as a <see cref="BackgroundService"/>, driving its
/// message-processing loop and settling each envelope automatically. Resolves
/// <typeparamref name="TConsumer"/> from a fresh DI scope per message so handlers can inject
/// scoped dependencies (e.g. a <c>DbContext</c>) the same way a controller action would.
/// </summary>
internal sealed class ConsumerBackgroundService<T, TConsumer> : BackgroundService
    where TConsumer : class, IMessageConsumer<T>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISubscriber<T> _subscriber;

    /// <param name="scopeFactory">Used to create a per-message DI scope.</param>
    /// <param name="subscriber">The subscriber to consume messages from.</param>
    public ConsumerBackgroundService(IServiceScopeFactory scopeFactory, ISubscriber<T> subscriber)
    {
        _scopeFactory = scopeFactory;
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
            await using var scope = _scopeFactory.CreateAsyncScope();
            try
            {
                var consumer = scope.ServiceProvider.GetRequiredService<TConsumer>();
                await consumer.HandleAsync(envelope, stoppingToken);
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
