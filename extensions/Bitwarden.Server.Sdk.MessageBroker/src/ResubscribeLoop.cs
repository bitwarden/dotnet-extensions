using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Keeps a hosted consumer's receive loop running through broker outages. A stream that fails
/// (the broker went away, refused the subscription, or could not be reached at all) is opened
/// again after a delay that doubles with each failure in a row, from one second up to thirty.
/// A stream that ends without failing ends the loop: that only happens when the host is closing.
/// </summary>
internal static class ResubscribeLoop
{
    internal static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    /// <summary>The delay after one more failure in a row: double the last, up to <see cref="MaximumDelay"/>.</summary>
    internal static TimeSpan NextDelay(TimeSpan delay) => delay * 2 < MaximumDelay ? delay * 2 : MaximumDelay;

    /// <param name="receiveAsync">
    /// Opens the stream and handles messages until it ends. Calls its first argument once the
    /// subscription is in place, and its second after each message, which resets the delay: a
    /// failure after a working stream is a new outage.
    /// </param>
    /// <param name="logger">Logs each failure and the delay before the next attempt.</param>
    /// <param name="source">The topic subscription or command queue, for the log and the readiness probe.</param>
    /// <param name="readiness">
    /// Told when the consumer is receiving and when it is failing. The consumer said it was expected
    /// when it was created.
    /// </param>
    /// <param name="topology">
    /// Waited for before the first attempt: open once the service has provisioned its topology, when it does.
    /// </param>
    /// <param name="timeProvider">Times the wait between attempts.</param>
    /// <param name="stoppingToken">Cancelled when the host stops.</param>
    internal static async Task RunAsync(
        Func<Action, Action, CancellationToken, Task> receiveAsync,
        ILogger logger,
        string source,
        SubscriptionReadiness readiness,
        TopologyGate? topology,
        TimeProvider timeProvider,
        CancellationToken stoppingToken)
    {
        if (topology is not null)
        {
            try
            {
                await topology.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var delay = InitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await receiveAsync(
                    () => readiness.Receiving(source),
                    () =>
                    {
                        delay = InitialDelay;
                        readiness.Receiving(source);
                    },
                    stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                readiness.Failing(source);
                logger.LogWarning(ex,
                    "Receiving from {Source} failed; subscribing again in {Delay}.", source, delay);
            }

            try
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            delay = NextDelay(delay);
        }
    }
}
