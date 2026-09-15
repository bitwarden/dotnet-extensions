using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Reply to a <see cref="Capability"/> or <see cref="PublisherJoin"/>. On <see cref="Go"/>
/// <c>false</c> the requester must exit non-zero; <see cref="Offenders"/> lists the
/// incompatible counterparties.
/// </summary>
internal sealed record NegotiationAck
{
    /// <summary><c>true</c> for a go, <c>false</c> for a no go.</summary>
    [JsonPropertyName("go")]
    public required bool Go { get; init; }

    /// <summary>
    /// Counterparties that do not share a wire name with the requester. Empty on go.
    /// <para>
    /// Replies to <see cref="Capability"/> list publishers; replies to
    /// <see cref="PublisherJoin"/> list subscribers.
    /// </para>
    /// </summary>
    [JsonPropertyName("offenders")]
    public IReadOnlyList<NegotiationIncompatibility> Offenders { get; init; } = [];
}

/// <summary>
/// One counterparty a <see cref="NegotiationAck"/> could not satisfy. Carries identity plus
/// declared wire names so the receiver can log without a second cache lookup.
/// </summary>
internal sealed record NegotiationIncompatibility
{
    /// <summary>
    /// The counterparty's <c>InstanceId</c> as it appeared in the counterparty's own
    /// <see cref="Capability"/> or <see cref="PublisherJoin"/>; typically
    /// <c>"{processDisplayName}-{guid}"</c>.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    /// <summary>The wire-name set the counterparty declared.</summary>
    [JsonPropertyName("wireNames")]
    public required HashSet<string> WireNames { get; init; }
}
