namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Builds the dependency-injection service keys used by the message broker.</summary>
/// <remarks>
/// A single topic can carry multiple families, so each registered
/// <see cref="Publisher{TFamily, TCeiling}"/> is keyed by a composite of the topic name and the
/// family's <see cref="IFamilyWireName{TSelf}.WireName"/>. Use this helper when resolving
/// publishers programmatically so the composite format stays in one place.
/// </remarks>
public static class MessageBrokerKeys
{
    /// <summary>
    /// Returns the service key under which <c>AddPublisher&lt;TFamily, TCeiling&gt;(topic)</c>
    /// registers its <see cref="Publisher{TFamily, TCeiling}"/>. The returned string has the
    /// shape <c>"{topic}#{WireName}"</c>. A <see cref="Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"/>
    /// at an injection site must spell this literally — attribute arguments must be compile-time
    /// constants, so the helper is only available for programmatic resolution.
    /// </summary>
    public static string Publisher<TFamily>(string topic)
        where TFamily : IFamilyWireName<TFamily>
        => $"{topic}#{TFamily.WireName}";
}
