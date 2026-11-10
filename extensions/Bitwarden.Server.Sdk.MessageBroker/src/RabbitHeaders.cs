namespace Microsoft.Extensions.DependencyInjection;

/// <summary>RabbitMQ header names shared between <see cref="RabbitPublisher{TPayload, TCeiling}"/> and <see cref="RabbitSubscriber{TPayload, TCeiling}"/>.</summary>
internal static class RabbitHeaders
{
    /// <summary>
    /// Carries the publisher's <see cref="IPayloadWireName{TSelf}.WireName"/>. Lets the subscriber
    /// verify that the message routed to it belongs to the family it was built to decode, even if
    /// the broker binding was misconfigured.
    /// </summary>
    internal const string PayloadFamily = "x-bw-payload-family";
}
