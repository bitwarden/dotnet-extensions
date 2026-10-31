using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// The creation of many resources.
/// </summary>
/// <typeparam name="T">The resource being created.</typeparam>
/// <remarks>
/// <para>
/// Targeted by <c>data</c> rather than by identifier or expression, because nothing exists yet
/// to name. Each item is shaped exactly as the single-resource create expects it.
/// </para>
/// <para>
/// Each item may carry a <c>lid</c>, a local identifier the caller chose, beside its
/// <c>type</c>, as JSON:API places it. The standards allow one so a caller can correlate what
/// came back with what it sent, and require an API that accepts it to echo it on the created
/// resource. It is read off the item here and kept in <see cref="Lids"/> rather than put on
/// <see cref="Resource"/>, because it means something only for the length of one bulk create
/// and every other resource in the platform would otherwise publish a field that is never set.
/// Answer with <see cref="BulkCreatedDocument{T}.For"/> and it goes back on each created
/// resource, in request order.
/// </para>
/// </remarks>
public sealed record BulkCreate<T> where T : Resource
{
    /// <summary>
    /// The resources to create.
    /// </summary>
    public required IReadOnlyList<T> Data { get; init; }

    /// <summary>
    /// The <c>lid</c> each item in <see cref="Data"/> carried, position for position, or null
    /// where it carried none.
    /// </summary>
    /// <remarks>
    /// Read from the body by the platform, which refuses one that is not a non-empty string or
    /// that two items share: a duplicated local identifier correlates nothing.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlyList<string?> Lids { get; internal set; } = [];

    /// <summary>
    /// The <c>lid</c> the item at a position carried, or null.
    /// </summary>
    /// <param name="index">Which item, from zero.</param>
    public string? LidAt(int index) => index < Lids.Count ? Lids[index] : null;

    /// <inheritdoc cref="BulkTargeting{T}.Atomicity"/>
    [JsonIgnore]
    public Atomicity Atomicity { get; internal set; } = Atomicity.AllOrNothing;
}
