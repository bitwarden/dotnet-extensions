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
}
