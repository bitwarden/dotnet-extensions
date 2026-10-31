namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// A page of a collection paged by cursor, with where the next page starts.
/// </summary>
/// <typeparam name="T">The resource.</typeparam>
/// <remarks>
/// <para>
/// Its own type, so an operation's published schema says which paging it uses and a caller is
/// told to expect <c>meta.cursor</c> rather than page numbers and a total.
/// </para>
/// <para>
/// No links and no count. The count is the cost cursor paging exists to avoid, and the only
/// link that could be built is <c>next</c>, which the cursor already is.
/// </para>
/// </remarks>
public sealed record CursorPagedResourceListDocument<T> where T : Resource
{
    /// <summary>
    /// The resources on this page.
    /// </summary>
    public required IReadOnlyList<T> Data { get; init; }

    /// <summary>
    /// Anything about the response that is not one of the resources in it.
    /// </summary>
    /// <remarks>
    /// Carries <c>pageSize</c>, <c>hasMore</c>, and, only when more follow, <c>cursor</c>: the
    /// value to send as <c>page[after]</c> for the next page.
    /// </remarks>
    [ReadOnly]
    public IDictionary<string, object?>? Meta { get; init; }
}
