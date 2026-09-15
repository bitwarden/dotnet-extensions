using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// A subscriber's known WireName set for a DataTopic. Sent on the control topic at startup
/// and each heartbeat. The active control consumer verifies at least one wire name overlaps
/// with every live publisher's set.
/// </summary>
internal sealed record Capability
{
    /// <summary>The DataTopic this subscriber consumes.</summary>
    [JsonPropertyName("dataTopic")]
    public required string DataTopic { get; init; }

    /// <summary>
    /// Composite instance identifier from <c>NegotiationOptions.InstanceId</c>; typically
    /// <c>"{processDisplayName}-{guid}"</c>. Unique to a process.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    /// <summary>
    /// Wire names the subscriber's payload chain can decode. Admission succeeds when this set
    /// overlaps with each publisher's set.
    /// </summary>
    [JsonPropertyName("wireNames")]
    public required HashSet<string> WireNames { get; init; }
}
