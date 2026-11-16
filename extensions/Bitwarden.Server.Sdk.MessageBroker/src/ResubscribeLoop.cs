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

    /// <param name="receiveAsync">
    /// Opens the stream and handles messages until it ends. Calls <c>onMessage</c> after each
    /// message, which resets the delay: a failure after a working stream is a new outage.
    /// </param>
    /// <param name="logger">Logs each failure and the delay before the next attempt.</param>
    /// <param name="source">The topic subscription or command queue, for the log.</param>
    /// <param name="timeProvider">Times the wait between attempts.</param>
    /// <param name="stoppingToken">Cancelled when the host stops.</param>
    internal static async Task RunAsync(
        Func<Action, CancellationToken, Task> receiveAsync,
        ILogger logger,
        string source,
        TimeProvider timeProvider,
        CancellationToken stoppingToken)
    {
        var delay = InitialDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await receiveAsync(() => delay = InitialDelay, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
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
            delay = delay * 2 < MaximumDelay ? delay * 2 : MaximumDelay;
        }
    }
}
