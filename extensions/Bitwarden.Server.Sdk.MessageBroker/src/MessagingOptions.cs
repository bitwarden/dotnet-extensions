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
    /// the <see cref="Azure.Core.TokenCredential"/> registered under
    /// <see cref="Microsoft.Extensions.DependencyInjection.MessageBrokerKeys.Credential"/> if there is
    /// one, otherwise the service's workload identity or managed identity in Azure (a user-assigned one
    /// chosen by <c>AZURE_CLIENT_ID</c>). Developer sign-ins and a service principal's secret in the
    /// environment are never used by default; register a credential to use one.
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
}
