using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Hosts an <see cref="IMessageConsumer{TPayload, TCeiling}"/> as a <see cref="BackgroundService"/>,
/// driving its message-processing loop and settling each envelope automatically. Resolves
/// <typeparamref name="TConsumer"/> from a fresh DI scope per message so handlers can inject
/// scoped dependencies (e.g. a <c>DbContext</c>) the same way a controller action would.
/// </summary>
internal sealed class ConsumerBackgroundService<TPayload, TCeiling, TConsumer> : BackgroundService
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
    where TConsumer : class, IMessageConsumer<TPayload, TCeiling>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISubscriber<TPayload, TCeiling> _subscriber;
    private readonly ILogger _logger;
    private readonly string _subscriptionKey;

    /// <param name="scopeFactory">Used to create a per-message DI scope.</param>
    /// <param name="subscriber">The subscriber to consume messages from.</param>
    /// <param name="logger">Logs a failed subscription before it is opened again.</param>
    /// <param name="subscriptionKey">The <c>{topic}/{subscription}</c> being consumed, for the log.</param>
    public ConsumerBackgroundService(
        IServiceScopeFactory scopeFactory, ISubscriber<TPayload, TCeiling> subscriber, ILogger logger, string subscriptionKey)
    {
        _scopeFactory = scopeFactory;
        _subscriber = subscriber;
        _logger = logger;
        _subscriptionKey = subscriptionKey;
    }

    // A subscription that fails (a broker outage, or a broker down at startup) is opened again
    // rather than faulting the service, which with .NET's defaults would stop the host.
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ResubscribeLoop.RunAsync(ReceiveAsync, _logger, _subscriptionKey, TimeProvider.System, stoppingToken);

    private async Task ReceiveAsync(Action onMessage, CancellationToken stoppingToken)
    {
        await foreach (var envelope in _subscriber.SubscribeAsync(stoppingToken))
        {
            onMessage();
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
