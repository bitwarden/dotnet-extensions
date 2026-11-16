namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// Settings for <see cref="ServiceBusTopology"/>.
/// </summary>
public sealed class ServiceBusOptions
{
    /// <summary>
    /// The most times a message is delivered, the first delivery included, before it's dead-lettered: each
    /// subscription's and queue's <c>MaxDeliveryCount</c>. At least 1. Default is 10.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;
}
