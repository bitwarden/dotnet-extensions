namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// Settings for <see cref="RabbitDefinitions.Build"/>.
/// </summary>
public sealed class RabbitDefinitionsOptions
{
    /// <summary>
    /// The most times a message is delivered, the first delivery included, before it's dead-lettered: the policies'
    /// <c>delivery-limit</c> is one less. At least 2. Default is 10, as Azure Service Bus's <c>MaxDeliveryCount</c>.
    /// </summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>
    /// The virtual host everything is declared in, which the definitions declare too. Default is <c>/</c>.
    /// </summary>
    public string VirtualHost { get; set; } = "/";
}
