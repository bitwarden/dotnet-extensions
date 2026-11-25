namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The wait a hosted consumer takes after its handler throws, before the message is redelivered.
/// </summary>
internal static class RetryDelay
{
    /// <summary>
    /// Waits <see cref="ConsumerRetryOptions.RetryDelayAfter"/>; a host that stops cuts the wait short.
    /// </summary>
    internal static async Task WaitAsync(ConsumerRetryOptions options, int deliveryCount, CancellationToken stoppingToken)
    {
        var delay = options.RetryDelayAfter(deliveryCount);
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
