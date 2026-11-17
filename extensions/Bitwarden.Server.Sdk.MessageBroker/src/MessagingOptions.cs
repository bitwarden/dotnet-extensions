namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>Options for configuring the message broker.</summary>
public class MessagingOptions
{
    /// <summary>
    /// The Azure Service Bus connection string, with a shared access key. When set, Azure Service
    /// Bus is used. Set this or <see cref="AzureServiceBusFullyQualifiedNamespace"/>, not both.
    /// </summary>
    public string? AzureServiceBusConnectionString { get; set; }

    /// <summary>
    /// The Azure Service Bus namespace's host name, such as <c>bitwarden.servicebus.windows.net</c>.
    /// When set, Azure Service Bus is used with the <see cref="Azure.Core.TokenCredential"/> the app
    /// registers under <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerKeys.Credential"/>;
    /// a host without one refuses to start.
    /// Set this or <see cref="AzureServiceBusConnectionString"/>, not both.
    /// </summary>
    public string? AzureServiceBusFullyQualifiedNamespace { get; set; }

    /// <summary>Whether Azure Service Bus is configured, by connection string or by namespace.</summary>
    internal bool UsesAzureServiceBus =>
        !string.IsNullOrEmpty(AzureServiceBusConnectionString) || !string.IsNullOrEmpty(AzureServiceBusFullyQualifiedNamespace);

    /// <summary>The Rabbit connection URI. When set and Azure Service Bus is not, Rabbit is used; when neither is set, an in-memory channel is used.</summary>
    public string? RabbitUri { get; set; }

    /// <summary>
    /// The maximum number of times a message is delivered before being routed to a dead-letter
    /// destination. Only the in-memory channel backend reads this value; the RabbitMQ and Azure
    /// Service Bus backends rely on broker-side configuration for delivery limits. Default is 10.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>
    /// How long, at most, an Azure Service Bus message's lock is renewed while a consumer handles it
    /// and waits to retry it. Set it no shorter than the longest
    /// <see cref="ConsumerRetryOptions.MaxRetryDelay"/>. Default is five minutes.
    /// </summary>
    public TimeSpan MaxLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);
}
