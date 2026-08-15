namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Options for configuring the message broker.</summary>
public class MessagingOptions
{
    /// <summary>The Azure Service Bus connection string. When set, Azure Service Bus is used (takes priority over Rabbit).</summary>
    public string? AzureServiceBusConnectionString { get; set; }

    /// <summary>The Rabbit connection URI. When set and <see cref="AzureServiceBusConnectionString"/> is not set, Rabbit is used; otherwise an in-memory channel is used.</summary>
    public string? RabbitUri { get; set; }

    /// <summary>
    /// The maximum number of times an in-memory channel message is redelivered before being
    /// discarded. Applies only to the in-memory channel backend; Azure Service Bus and Rabbit
    /// manage redelivery limits on the broker side. Default is 10.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;
}
