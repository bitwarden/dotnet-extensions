using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// A booting publisher's declared wire-name set for a <c>(DataTopic, FamilyWireName)</c>.
/// Structurally identical to <see cref="Capability"/> but a distinct type so the handler
/// routes on it: a <see cref="PublisherJoin"/> is checked against cached subscribers, a
/// <see cref="Capability"/> against cached publishers.
/// </summary>
internal sealed record PublisherJoin
{
    /// <summary>The data-topic this publisher produces.</summary>
    [JsonPropertyName("dataTopic")]
    public required string DataTopic { get; init; }

    /// <summary>
    /// The family this publisher produces, from <c>IFamilyWireName.WireName</c>.
    /// Admission runs within one family — a <c>user.created</c> publisher is never checked
    /// against <c>user.deleted</c> subscribers on the same topic.
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
    /// Wire names the publisher's payload chain can produce. Admission succeeds when this set
    /// overlaps with each cached same-family subscriber's set.
    /// </summary>
    [JsonPropertyName("wireNames")]
    public required HashSet<string> WireNames { get; init; }
}
