using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The response documents a handler returns, named for the standard operations.
/// </summary>
/// <remarks>
/// <para>
/// With <c>using static Bitwarden.Server.Sdk.Service.StandardResponses;</c> a handler's body
/// reads the way its attribute does: <c>return CreateResponse(created);</c>.
/// </para>
/// <para>
/// Static, so they cannot read the request; the platform does that when the result is written.
/// They are deliberately thin: a create's <c>Location</c> is derived from the request path and
/// the resource's identifier, a page's links from the request's own URL, and the status code
/// from the operation's attribute, by the standard endpoint filter, so returning a document
/// directly still behaves correctly.
/// </para>
/// </remarks>
public static class StandardResponses
{
    /// <summary>
    /// The body of a read by identifier the platform serves (<c>AddStandardRead</c>).
    /// </summary>
    /// <typeparam name="T">The resource.</typeparam>
    /// <returns>Never returns: the platform answers before the handler's body would run.</returns>
    /// <remarks>
    /// <para>
    /// A resource declared with the platform's read is read by identifier one way only, the
    /// declaration's, so the answer a cache holds for it is the answer the service gives. The
    /// handler stays, because it is where the route, the operation and its documentation are
    /// declared; its body is never run, and this says so where a reader would otherwise look for
    /// the rule.
    /// </para>
    /// <example>
    /// <code>
    /// [StandardReadOne]
    /// public static Task&lt;ResourceDocument&lt;Group&gt;&gt; GetGroup(string id, Fields&lt;Group&gt; fields = default)
    ///     => ServedByPlatform&lt;Group&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="System.Diagnostics.UnreachableException">Always: the platform did not answer.</exception>
    public static Task<ResourceDocument<T>> ServedByPlatform<T>()
        where T : Resource =>
        throw new System.Diagnostics.UnreachableException(
            $"{typeof(T).Name} is read by identifier by the platform, but no standard read is declared for it. "
            + "Declare it with AddStandardRead (or AddOwnedCache).");

    /// <summary>
    /// Builds the response to a create.
    /// </summary>
    /// <param name="resource">The resource that was created, including its assigned ID.</param>
    /// <param name="location">
    /// Where the created resource can be read. Omit it and the platform derives it from the
    /// request path and the resource's ID, which is the right answer whenever the create posted to
    /// the resource's own collection.
    /// </param>
    /// <param name="meta">Anything about the response that is not part of the resource.</param>
    /// <returns>The response document.</returns>
    public static ResourceDocument<T> CreateResponse<T>(
        T resource,
        string? location = null,
        IDictionary<string, object?>? meta = null)
        where T : Resource =>
        new() { Data = resource, Meta = meta, Location = string.IsNullOrEmpty(location) ? null : location };

    /// <summary>
    /// Builds the response to a read of a page of resources.
    /// </summary>
    /// <param name="page">The resources found, and how many there are in all.</param>
    /// <param name="requested">The page the caller asked for.</param>
    /// <param name="meta">Anything about the response that is not part of the resources.</param>
    /// <returns>The response document; its links to the rest of the collection are built from the request.</returns>
    public static PagedResourceListDocument<T> ReadManyResponse<T>(
        Paged<T> page,
        Page requested,
        IDictionary<string, object?>? meta = null)
        where T : Resource
        => ReadManyResponse(page, requested, default(Fields<T>), meta);

    /// <summary>
    /// Builds the response to a read of a page of resources, each with only the attributes asked for.
    /// </summary>
    /// <param name="page">The resources found, and how many there are in all.</param>
    /// <param name="requested">The page the caller asked for.</param>
    /// <param name="fields">The caller's sparse fieldset: every attribute when they sent none.</param>
    /// <param name="meta">Anything about the response that is not part of the resources.</param>
    /// <returns>The response document; its links to the rest of the collection are built from the request.</returns>
    /// <remarks>
    /// The links carry whatever filtering and sorting the caller applied, so a caller following
    /// <c>next</c> stays in the same query.
    /// </remarks>
    public static PagedResourceListDocument<T> ReadManyResponse<T>(
        Paged<T> page,
        Page requested,
        Fields<T> fields,
        IDictionary<string, object?>? meta = null)
        where T : Resource
    {
        ArgumentNullException.ThrowIfNull(requested);

        // pageCount is at least one: an empty collection is one empty page, which is also what
        // the last link points at.
        var last = Math.Max(1, (int)Math.Ceiling(page.Total / (double)requested.Size));

        // The four fields the standards name for offset paging. They go in meta because they
        // describe the response rather than any resource in it, which is what meta is for.
        var combined = meta is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(meta);
        combined["pageCount"] = last;
        combined["pageNumber"] = requested.Number;
        combined["pageSize"] = requested.Size;
        combined["totalCount"] = page.Total;

        return new PagedResourceListDocument<T>
        {
            Data = page.Items,
            Fields = fields.Names,
            Meta = combined,
            PendingLinks = (requested, last),
        };
    }

    /// <summary>
    /// Builds the response to a read of a page of resources paged by cursor.
    /// </summary>
    /// <param name="page">The resources on the page, and where the next one starts.</param>
    /// <param name="requested">The page the caller asked for.</param>
    /// <param name="meta">Anything else about the response that is not part of the resources.</param>
    /// <returns>The response document.</returns>
    /// <remarks>
    /// <c>cursor</c> is present only when more follow. The standards require the last page to
    /// omit it rather than send null, so a caller can test for the member itself.
    /// </remarks>
    public static CursorPagedResourceListDocument<T> ReadManyResponse<T>(
        CursorPaged<T> page,
        CursorPage requested,
        IDictionary<string, object?>? meta = null)
        where T : Resource
    {
        ArgumentNullException.ThrowIfNull(requested);

        var combined = meta is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(meta);
        combined["pageSize"] = requested.Size;
        combined["hasMore"] = page.HasMore;

        if (page.Cursor is { } cursor)
        {
            combined["cursor"] = cursor;
        }

        return new CursorPagedResourceListDocument<T> { Data = page.Items, Meta = combined };
    }

    /// <summary>
    /// Builds the response to a read of one resource.
    /// </summary>
    /// <param name="resource">The resource that was read.</param>
    /// <param name="meta">Anything about the response that is not part of the resource.</param>
    /// <returns>The response document.</returns>
    public static ResourceDocument<T> ReadOneResponse<T>(T resource, IDictionary<string, object?>? meta = null)
        where T : Resource
        => new() { Data = resource, Meta = meta };

    /// <summary>
    /// Builds the response to a read of one resource, with only the attributes asked for.
    /// </summary>
    /// <param name="resource">The resource that was read.</param>
    /// <param name="fields">The caller's sparse fieldset: every attribute when they sent none.</param>
    /// <param name="meta">Anything about the response that is not part of the resource.</param>
    /// <returns>The response document, which leaves out every attribute not in <paramref name="fields"/>.</returns>
    public static ResourceDocument<T> ReadOneResponse<T>(T resource, Fields<T> fields, IDictionary<string, object?>? meta = null)
        where T : Resource
        => new() { Data = resource, Meta = meta, Fields = fields.Names };

    /// <summary>
    /// Builds the response to a read of many resources.
    /// </summary>
    /// <param name="resources">The resources that were read. Empty is a valid answer, not a <c>404</c>.</param>
    /// <param name="meta">Anything about the response that is not part of the resources.</param>
    /// <returns>The response document.</returns>
    public static ResourceListDocument<T> ReadManyResponse<T>(
        IReadOnlyList<T> resources,
        IDictionary<string, object?>? meta = null)
        where T : Resource
        => new() { Data = resources, Meta = meta };

    /// <summary>
    /// Builds the response to an action that returns the resource it acted on.
    /// </summary>
    /// <param name="resource">The resource as it now stands, after the action.</param>
    /// <param name="meta">Anything about the response that is not part of the resource.</param>
    /// <returns>The response document.</returns>
    public static ResourceDocument<T> ActionResponse<T>(T resource, IDictionary<string, object?>? meta = null)
        where T : Resource
        => new() { Data = resource, Meta = meta };

    /// <summary>
    /// Builds the response to an update.
    /// </summary>
    /// <param name="resource">The resource as it now stands, after the update.</param>
    /// <param name="meta">Anything about the response that is not part of the resource.</param>
    /// <returns>The response document.</returns>
    public static ResourceDocument<T> UpdateResponse<T>(T resource, IDictionary<string, object?>? meta = null)
        where T : Resource
        => new() { Data = resource, Meta = meta };
}
