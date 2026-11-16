namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Builds the dependency-injection service keys used by the message broker.</summary>
/// <remarks>
/// A single topic can carry multiple payload families, so each registered
/// <see cref="Publisher{TPayload, TCeiling}"/> is keyed by a composite of the topic name and the
/// payload's <see cref="IPayloadWireName{TSelf}.WireName"/>. Use this helper when resolving
/// publishers programmatically so the composite format stays in one place.
/// </remarks>
public static class MessageBrokerKeys
{
    /// <summary>
    /// Returns the service key under which <c>AddPublisher&lt;TPayload, TCeiling&gt;(topic)</c>
    /// registers its <see cref="Publisher{TPayload, TCeiling}"/>. The returned string has the
    /// shape <c>"{topic}#{WireName}"</c>. A <see cref="Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"/>
    /// at an injection site must spell this literally — attribute arguments must be compile-time
    /// constants, so the helper is only available for programmatic resolution.
    /// </summary>
    public static string Publisher<TPayload>(string topic)
        where TPayload : IPayloadWireName<TPayload>
        => $"{topic}#{TPayload.WireName}";

    /// <summary>
    /// The service key under which an app registers the <c>Azure.Core.TokenCredential</c>
    /// the message broker uses for an Azure Service Bus namespace
    /// (<see cref="MessagingOptions.AzureServiceBusFullyQualifiedNamespace"/>):
    /// <c>services.AddKeyedSingleton&lt;TokenCredential&gt;(MessageBrokerKeys.Credential, credential)</c>.
    /// Required with a namespace: the broker has no credential of its own. Keyed, so a credential the app
    /// registers for something else is never taken for the broker.
    /// </summary>
    public const string Credential = "Bitwarden.Server.Sdk.MessageBroker";
}
