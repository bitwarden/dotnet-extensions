using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// A subscriber's known WireName set for a <c>(DataTopic, FamilyWireName)</c>. Sent on the
/// control topic at startup and each heartbeat. The active control consumer verifies at least
/// one wire name overlaps with every live publisher of the same family.
/// </summary>
internal sealed record Capability
{
    /// <summary>The DataTopic this subscriber consumes.</summary>
    [JsonPropertyName("dataTopic")]
    public required string DataTopic { get; init; }

    /// <summary>
    /// The family this subscriber decodes, from <c>IFamilyWireName.WireName</c>.
    /// Admission runs within one family — a <c>user.created</c> subscriber is never checked
    /// against <c>user.deleted</c> publishers on the same topic.
    /// </summary>
    [JsonPropertyName("familyWireName")]
    public required string FamilyWireName { get; init; }

    /// <summary>
    /// Composite instance identifier from <c>NegotiationOptions.InstanceId</c>; typically
    /// <c>"{processDisplayName}-{guid}"</c>. Unique to a process.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    /// <summary>
    /// Wire names the subscriber's payload chain can decode. Admission succeeds when this set
    /// overlaps with each same-family publisher's set.
    /// </summary>
    [JsonPropertyName("wireNames")]
    public required HashSet<string> WireNames { get; init; }
}
