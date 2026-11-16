using System.Diagnostics;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Runs a hosted consumer on one received message, the same way for a topic subscription
/// (<see cref="ConsumerBackgroundService{TPayload, TCeiling, TConsumer}"/>) and a command queue
/// (<see cref="CommandFamilyHandler{TPayload, TCeiling, TConsumer}"/>): the receive span is current
/// while it runs, it gets a scope of its own, and the message is completed when it returns and
/// requeued when it throws, after the retry delay (<see cref="RetryDelay"/>).
/// </summary>
internal static class ConsumerInvocation
{
    public static async Task RunAsync<TPayload, TCeiling, TConsumer>(
        IServiceScopeFactory scopeFactory,
        Envelope<TPayload, TCeiling> envelope,
        MessagingOptions options,
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
            var consumer = scope.ServiceProvider.GetRequiredService<TConsumer>();
            await consumer.HandleAsync(envelope, stoppingToken);
            // Settle with CancellationToken.None: passing along a stopped token after a
            // successful handler would leave the message un-acked and unsettled.
            await envelope.CompleteAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            await RetryDelay.WaitAsync(options, envelope.DeliveryCount, stoppingToken);
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
