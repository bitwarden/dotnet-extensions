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
    /// destination. For the in-memory channel backend this controls how many times the message
    /// is redelivered before being discarded. For RabbitMQ this is applied as
    /// <c>x-delivery-limit</c> on each quorum queue. Azure Service Bus manages the delivery
    /// limit on the topic/subscription directly and does not use this value. Default is 10.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;
}
