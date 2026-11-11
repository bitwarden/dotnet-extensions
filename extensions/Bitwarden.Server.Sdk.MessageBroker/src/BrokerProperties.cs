namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Per-message broker properties shared across backends so publishers and subscribers agree on names.</summary>
internal static class BrokerProperties
{
    /// <summary>
    /// Carries the publisher's <see cref="IFamilyWireName{TSelf}.WireName"/>. Lets each
    /// subscriber verify that the message routed to it belongs to the family it was built to
    /// decode, even if the broker binding or subscription filter was misconfigured. Rides as a
    /// RabbitMQ header and as an Azure Service Bus <c>ApplicationProperties</c> entry.
    /// </summary>
    internal const string Family = "x-bw-family";

    /// <summary>
    /// W3C traceparent — the serialized <see cref="System.Diagnostics.Activity.Id"/> of the publish
    /// span. Subscribers parse it to open the consume span as a child of the producer span. Value
    /// pinned by the W3C Trace Context specification, not Bitwarden.
    /// </summary>
    internal const string TraceParent = "traceparent";
}
