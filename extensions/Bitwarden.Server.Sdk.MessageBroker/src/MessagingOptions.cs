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

    /// <summary>
    /// Whether Azure Service Bus is configured, by connection string or by namespace.
    /// </summary>
    internal bool UsesAzureServiceBus =>
        !string.IsNullOrEmpty(AzureServiceBusConnectionString) || !string.IsNullOrEmpty(AzureServiceBusFullyQualifiedNamespace);

    /// <summary>
    /// The Rabbit connection URI. When set and Azure Service Bus is not, Rabbit is used; when neither is set, an in-memory channel is used.
    /// </summary>
    public string? RabbitUri { get; set; }

    /// <summary>
    /// The maximum number of times a message is delivered before being routed to a dead-letter
    /// destination. The in-memory channel backend enforces it. On RabbitMQ and Azure Service
    /// Bus the broker enforces its own limit; a service that provisions its topology
    /// (<see cref="AutoProvisionTopology"/>) gives the queues and subscriptions it creates this one, and
    /// leaves existing ones as they are. Default is 10.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>
    /// How long, at most, an Azure Service Bus message's lock is renewed while a consumer handles it
    /// and waits to retry it. Set it no shorter than the longest
    /// <see cref="ConsumerRetryOptions.MaxRetryDelay"/>. Default is five minutes.
    /// </summary>
    public TimeSpan MaxLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether a service that doesn't provision its topology (<see cref="AutoProvisionTopology"/>) checks,
    /// once it first reaches its broker, that the topics, subscriptions and queues its registrations need
    /// exist, and stops, naming what is missing, if they don't. A broker
    /// that can't be reached doesn't stop the service; the check waits until it can. Default is
    /// <see langword="true"/>; turn it off for a broker whose topology is declared after the service
    /// starts, such as in tests.
    /// </summary>
    public bool VerifyTopologyAtStartup { get; set; } = true;

    /// <summary>
    /// Whether the service creates the missing topics, subscriptions and queues its registrations need,
    /// once it first reaches its broker. It needs the rights to create them: configure on RabbitMQ,
    /// the Manage right on Azure Service Bus. Unset, a self-hosted service provisions and any
    /// other doesn't.
    /// </summary>
    public bool? AutoProvisionTopology { get; set; }
}
