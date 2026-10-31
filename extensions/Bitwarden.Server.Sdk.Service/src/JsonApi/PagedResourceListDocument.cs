using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A page of a collection, with the means to reach the rest of it.
/// </summary>
/// <typeparam name="T">The resource.</typeparam>
/// <remarks>
/// Distinct from <see cref="ResourceListDocument{T}"/> rather than a flag on it, so that an operation's
/// published schema says which it is. A caller reading the document for a paged operation sees
/// <c>links</c> and the paging fields of <c>meta</c> described; one reading an unpaged operation is not told to
/// expect fields that will never arrive.
/// </remarks>
public sealed record PagedResourceListDocument<T> : ISparseDocument, Endpoints.IDeferredLinks where T : Resource
{
    /// <summary>
    /// The resources on this page.
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
    /// <remarks>
    /// Carries the standards' four: <c>pageCount</c>, <c>pageNumber</c>, <c>pageSize</c> and
    /// <c>totalCount</c>, how many resources match across every page.
    /// </remarks>
    [ReadOnly]
    public IDictionary<string, object?>? Meta { get; init; }

    /// <summary>
    /// Where the rest of the collection is.
    /// </summary>
    /// <remarks>
    /// A caller following links does not have to know how the service pages, or reimplement the
    /// arithmetic, which is the point of sending them rather than only a total.
    /// </remarks>
    [ReadOnly]
    public PageLinks? Links { get; init; }

    /// <summary>
    /// The page this answers and the last page there is, until the links are built from the request.
    /// </summary>
    [JsonIgnore]
    internal (Querying.Page Requested, int Last)? PendingLinks { get; init; }

    /// <inheritdoc />
    object Endpoints.IDeferredLinks.WithLinks(Microsoft.AspNetCore.Http.HttpRequest request) =>
        PendingLinks is { } pending
            ? this with { Links = Endpoints.PageLinkBuilder.Links(request, pending.Requested, pending.Last), PendingLinks = null }
            : this;
}
