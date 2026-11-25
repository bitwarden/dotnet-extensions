namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Whether, and how, a hosted consumer retries a message its handler threw on. Named
/// <c>{topic}/{subscription}</c> for a subscription, or after the queue for a command queue.
/// </summary>
/// <remarks>
/// By default a message whose handler throws is dead-lettered at once. While a consumer waits to
/// retry a message it takes no other from that subscription or queue.
/// </remarks>
public sealed class ConsumerRetryOptions
{
    /// <summary>
    /// How many times a message is delivered before a failure dead-letters it. Default is one: no
    /// retry. The broker's own delivery limit still applies.
    /// </summary>
    public int MaxDeliveries { get; set; } = 1;

    /// <summary>
    /// How long the consumer waits after its handler throws before returning the message: this
    /// long after the first delivery, doubling with each delivery up to <see cref="MaxRetryDelay"/>.
    /// Default is no wait.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// The longest <see cref="RetryDelay"/> grows to. Default is thirty seconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    internal TimeSpan RetryDelayAfter(int deliveryCount)
    {
        if (RetryDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var doublings = Math.Clamp(deliveryCount - 1, 0, 30);
        var ticks = RetryDelay.Ticks * Math.Pow(2, doublings);
        return ticks >= MaxRetryDelay.Ticks ? MaxRetryDelay : TimeSpan.FromTicks((long)ticks);
    }
}
