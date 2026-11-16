namespace Microsoft.Extensions.DependencyInjection;

/// <summary>The wait a hosted consumer takes after its handler throws, before the message is redelivered.</summary>
internal static class RetryDelay
{
    /// <summary>
    /// Waits <see cref="MessagingOptions.RetryDelayAfter"/> for a message on its
    /// <paramref name="deliveryCount"/>-th delivery. A host that stops during the wait cuts it
    /// short; the caller still returns the message, so the next consumer gets it at once.
    /// </summary>
    internal static async Task WaitAsync(MessagingOptions options, int deliveryCount, CancellationToken stoppingToken)
    {
        var delay = options.RetryDelayAfter(deliveryCount);
        if (delay <= TimeSpan.Zero)
            return;
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
