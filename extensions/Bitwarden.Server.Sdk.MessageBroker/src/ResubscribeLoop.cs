using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Keeps a hosted consumer's receive loop running through broker outages. A stream that fails is
/// opened again after a delay that doubles with each failure in a row, from one second up to thirty.
/// </summary>
internal static class ResubscribeLoop
{
    internal static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    /// <param name="receiveAsync">
    /// Opens the stream and handles messages until it ends. Calls <c>onMessage</c> after each
    /// message, which resets the delay: a failure after a working stream is a new outage.
    /// </param>
    /// <param name="logger">The logger for the consumer.</param>
    /// <param name="source">The topic subscription being consumed.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="stoppingToken">The token that stops the loop.</param>
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
