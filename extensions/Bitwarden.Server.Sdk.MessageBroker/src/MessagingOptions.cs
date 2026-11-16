namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Options for configuring the message broker.</summary>
public class MessagingOptions
{
    /// <summary>The Azure Service Bus connection string. When set, Azure Service Bus is used (takes priority over Rabbit).</summary>
    public string? AzureServiceBusConnectionString { get; set; }

    /// <summary>The Rabbit connection URI. When set and <see cref="AzureServiceBusConnectionString"/> is not set, Rabbit is used; otherwise an in-memory channel is used.</summary>
    public string? RabbitUri { get; set; }

    /// <summary>
    /// The maximum number of times a message is delivered before being routed to a dead-letter
    /// destination. Only the in-memory channel backend reads this value; the RabbitMQ and Azure
    /// Service Bus backends rely on broker-side configuration for delivery limits. Default is 10.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>
    /// How long a hosted consumer (<c>AddMessageConsumer</c>, <c>AddCommandConsumer</c>) waits
    /// after its handler throws before the message is returned for redelivery. The wait doubles
    /// with each delivery, up to <see cref="MaxRetryDelay"/>. While it waits the consumer takes no
    /// other message from that subscription or queue, so a failing dependency slows the consumer
    /// rather than burning through the delivery limit. Zero redelivers at once. Default is one second.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest <see cref="RetryDelay"/> grows to. Default is thirty seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long, at most, an Azure Service Bus message's lock is renewed while a consumer handles it
    /// and waits to retry it. After that the lock is left to expire, so a handler that hangs gives
    /// the message back to the broker for another delivery rather than holding it until the host
    /// stops. Must be at least <see cref="MaxRetryDelay"/>. Default is five minutes, as for the
    /// Azure SDK's own processor.
    /// </summary>
    public TimeSpan MaxLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The wait before redelivering a message whose <paramref name="deliveryCount"/>-th delivery
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
