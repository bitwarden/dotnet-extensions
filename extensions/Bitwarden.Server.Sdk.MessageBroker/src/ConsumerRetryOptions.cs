namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Whether, and how, a hosted consumer retries a message its handler threw on. Set for one topic
/// subscription (named <c>{topic}/{subscription}</c>) or one command queue (named after the queue),
/// through the <c>configureRetry</c> argument of <c>AddMessageConsumer</c> or <c>AddCommandConsumer</c>,
/// or as named options from configuration.
/// </summary>
/// <remarks>
/// By default a consumer doesn't retry: a message whose handler throws is dead-lettered at once.
/// A consumer handles one message at a time, so while it waits to retry one it takes no other
/// from that subscription or queue; on a command queue that holds up every command family on it.
/// </remarks>
public sealed class ConsumerRetryOptions
{
    /// <summary>
    /// How many times a message is delivered before a failure dead-letters it instead of returning
    /// it for another delivery. Default is one: no retry. The broker's own delivery limit (the
    /// RabbitMQ <c>delivery-limit</c> policy, Azure Service Bus's <c>MaxDeliveryCount</c>) still
    /// applies, so a value above it has no effect.
    /// </summary>
    public int MaxDeliveries { get; set; } = 1;

    /// <summary>
    /// How long the consumer waits after its handler throws before returning the message: this
    /// long after the first delivery, doubling with each delivery up to <see cref="MaxRetryDelay"/>.
    /// Default is no wait.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;

    /// <summary>The longest <see cref="RetryDelay"/> grows to. Default is thirty seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The wait before returning a message whose <paramref name="deliveryCount"/>-th delivery
    /// failed: <see cref="RetryDelay"/> doubled once for each earlier delivery, capped at
    /// <see cref="MaxRetryDelay"/>.
    /// </summary>
    internal TimeSpan RetryDelayAfter(int deliveryCount)
    {
        if (RetryDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;
        var doublings = Math.Clamp(deliveryCount - 1, 0, 30);
        var ticks = RetryDelay.Ticks * Math.Pow(2, doublings);
        return ticks >= MaxRetryDelay.Ticks ? MaxRetryDelay : TimeSpan.FromTicks((long)ticks);
    }
}
