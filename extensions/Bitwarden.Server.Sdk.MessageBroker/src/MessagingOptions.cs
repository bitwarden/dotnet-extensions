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
    /// When set, Azure Service Bus is used with a token credential instead of a shared access key:
    /// the <see cref="Azure.Core.TokenCredential"/> the app registers under
    /// <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerKeys.Credential"/>, typically its
    /// managed identity. A host with a namespace and no registered credential refuses to start.
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
    /// and waits to retry it. After that the lock is left to expire, so a handler that hangs gives
    /// the message back to the broker for another delivery rather than holding it until the host
    /// stops. Set it no shorter than the longest <see cref="ConsumerRetryOptions.MaxRetryDelay"/>, or a
    /// message waiting to be retried can lose its lock and be delivered again sooner. Default is five
    /// minutes, as for the Azure SDK's own processor.
    /// </summary>
    public TimeSpan MaxLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether the service checks, once it first reaches its broker, that the topics, subscriptions and
    /// queues its registrations need exist, and stops, naming what is missing, if they don't. A broker
    /// that can't be reached doesn't stop the service; the check waits until it can. Default is
    /// <see langword="true"/>; turn it off for a broker whose topology is declared after the service
    /// starts, such as in tests.
    /// </summary>
    public bool VerifyTopologyAtStartup { get; set; } = true;
}
