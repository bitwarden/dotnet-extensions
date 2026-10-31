using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A collection of resources, returned whole.
/// </summary>
/// <typeparam name="T">The resource.</typeparam>
/// <remarks>
/// For a collection whose size is bounded by something other than the caller: a fixed set, or
/// one an operation's own scope keeps small. A collection that can grow with the data should be
/// a <see cref="PagedResourceListDocument{T}"/> instead, so that a caller is never handed an unbounded
/// response.
/// </remarks>
public sealed record ResourceListDocument<T> : ISparseDocument where T : Resource
{
    /// <summary>
    /// The resources.
    /// </summary>
    public required IReadOnlyList<T> Data { get; init; }

    /// <summary>
    /// The attributes a sparse fieldset asked for, or null for every one.
    /// </summary>
    /// <remarks>
    /// Not part of the document: the platform leaves the other attributes out of each resource when
    /// it writes it. Set from the operation's <see cref="Fields{T}"/>.
    /// </remarks>
    [JsonIgnore]
    public IReadOnlySet<string>? Fields { get; init; }

    /// <summary>
    /// Anything about the response that is not one of the resources in it.
    /// </summary>
    [ReadOnly]
    public IDictionary<string, object?>? Meta { get; init; }
}
