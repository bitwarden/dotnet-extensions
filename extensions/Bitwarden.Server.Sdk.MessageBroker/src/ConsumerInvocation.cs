using System.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Runs a hosted consumer on one received message, for topic subscriptions and command queues alike.
/// </summary>
internal static class ConsumerInvocation
{
    public static async Task RunAsync<TPayload, TCeiling, TConsumer>(
        IServiceScopeFactory scopeFactory,
        Envelope<TPayload, TCeiling> envelope,
        ConsumerRetryOptions retry,
        CancellationToken stoppingToken)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TConsumer : class, IMessageConsumer<TPayload, TCeiling>
    {
        // Make the consumer span current so telemetry emitted inside HandleAsync (nested
        // activities, HttpClient spans, EF Core command spans, scoped logs) is parented to
        // the receive span. The backends create the Activity on the receiver's execution
        // context, which an async enumerator does not flow out to its caller's loop body.
        var previousActivity = Activity.Current;
        Activity.Current = envelope.Activity;
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            Exception? failure = null;
            var stopped = false;
            try
            {
                var consumer = scope.ServiceProvider.GetRequiredService<TConsumer>();
                await consumer.HandleAsync(envelope, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host is stopping and the handler gave up because of it: not a failure.
                stopped = true;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                if (stopped)
                {
                    // Return the message at once, for the next instance (or, in memory, escrow),
                    // without the retry delay or the dead-letter decision.
                    if (!envelope.IsSettled)
                        await envelope.RequeueAsync("The consumer stopped.", CancellationToken.None);
                }
                else if (failure is null)
                {
                    // Settle with CancellationToken.None: passing along a stopped token after a
                    // successful handler would leave the message un-acked and unsettled.
                    await envelope.CompleteAsync(CancellationToken.None);
                }
                else if (!envelope.IsSettled)
                {
                    // A handler that settled the message itself (dead-lettered it, say) before
                    // throwing has decided its fate. Otherwise retry it if its options allow
                    // another delivery, and dead-letter it if not. The reason names the exception's
                    // type only: its message can carry data from the payload.
                    var reason = $"The consumer threw {failure.GetType().Name} on delivery {envelope.DeliveryCount}.";
                    if (envelope.DeliveryCount >= retry.MaxDeliveries)
                    {
                        await envelope.DeadLetterAsync(reason, CancellationToken.None);
                    }
                    else
                    {
                        await RetryDelay.WaitAsync(retry, envelope.DeliveryCount, stoppingToken);
                        await envelope.RequeueAsync(reason, CancellationToken.None);
                    }
                }
            }
            catch (Exception)
            {
                // Settlement failed (broker disconnected, lock expired, etc.). The broker
                // will redeliver the message when the lock times out. Swallow so a transient
                // settlement error doesn't kill the consumer permanently, and don't wait: the
                // handler succeeded, or the message is already on its way back.
            }
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }
}
