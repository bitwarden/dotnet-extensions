using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// A subscriber's clean-shutdown withdrawal. Sent on the control topic during graceful
/// shutdown so the SAC-holding negotiation listener removes this instance's entry from the
/// fleet-state cache immediately rather than waiting for TTL expiration. The remove is
/// best-effort — if the send or ack fails, the entry simply lingers until its TTL runs out.
/// </summary>
internal sealed record SubscriberLeave
{
    /// <summary>The data-topic this subscriber is leaving.</summary>
    [JsonPropertyName("dataTopic")]
    public required string DataTopic { get; init; }

    /// <summary>
    /// Composite instance identifier from <c>NegotiationOptions.InstanceId</c>; typically
    /// <c>"{processDisplayName}-{guid}"</c>. Matches the identifier used on the corresponding
    /// <see cref="Capability"/>.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }
}
