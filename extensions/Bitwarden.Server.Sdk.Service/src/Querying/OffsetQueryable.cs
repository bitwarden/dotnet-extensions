namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Pages a query by offset, and counts the whole collection only when the page cannot say how
/// big it is.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, like <see cref="CursorQueryable"/>, because the platform does not know how a service
/// reads its database. <see cref="Page{TEntity}"/> takes the page from an ordered query; the service
/// materialises it however it materialises anything; <see cref="ToPagedAsync{TRow, TResource}"/>
/// works out the total, running the service's count only if it must:
/// </para>
/// <code>
/// var folders = database.Folders.AsNoTracking();
/// var rows = await folders
///     .SortedBy(sort ?? Sort.Default&lt;Folder&gt;(), entity => entity.Id)
///     .Page(page)
///     .ToListAsync(cancellationToken);
///
/// return await rows.ToPagedAsync(page, Shape, folders.CountAsync, cancellationToken);
/// </code>
/// <para>
/// <b>When the count runs.</b> A page that comes back with fewer rows than it holds is the last
/// one, so the total is the rows before it plus the rows on it, and no count runs. Almost every
/// list a client reads fits on its first page, so almost every list read costs one query instead
/// of two. Two cases still count: a full page, where more may follow, and an empty page past the
/// first, which says only that the collection ends somewhere before it.
/// </para>
/// <para>
/// A full page counts because every offset-paged operation publishes <c>totalCount</c>,
/// <c>pageCount</c> and a <c>last</c> link, so every caller of one is a caller who wants the
/// total. A collection whose callers read in order and never want a total pages by
/// <see cref="CursorPage"/> instead, which never counts.
/// </para>
/// <para>
/// <b>When a key bounds the answer.</b> A filter by identifiers matches at most one row for each
/// identifier, and a filter by a uniquely indexed column at most one. A service that knows such a
/// bound passes it as <c>atMost</c>: a page that reaches it holds the last row there can be, so
/// even a full page needs no count. Only a key the schema enforces is a bound; a rule the code
/// keeps is not, since a row that breaks it would then be silently left out of the total.
/// </para>
/// <para>
/// The count is passed in rather than run here so the query being counted is the service's own,
/// and so this package needs no data access library. Count the query the page was taken from,
/// without the page: the rows it filters are the rows the total describes.
/// </para>
/// </remarks>
public static class OffsetQueryable
{
    /// <summary>
    /// Takes the page from a query that is already filtered and ordered.
    /// </summary>
    /// <typeparam name="TEntity">The row being paged.</typeparam>
    /// <param name="ordered">The query, in the order it pages in.</param>
    /// <param name="page">The page asked for.</param>
    /// <returns>The query to materialise.</returns>
    public static IQueryable<TEntity> Page<TEntity>(this IQueryable<TEntity> ordered, Page page)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(page);

        return ordered.Skip(page.Skip).Take(page.Size);
    }

    /// <summary>
    /// The total, when the rows a page came back with settle it; null when only a count can.
    /// </summary>
    /// <param name="page">The page asked for.</param>
    /// <param name="returned">How many rows it came back with.</param>
    /// <param name="atMost">
    /// The most rows the query can match, when a key the schema enforces guarantees it: the number
    /// of distinct identifiers it filters by, or one for a uniquely indexed value. Null when nothing
    /// bounds it.
    /// </param>
    /// <returns>The rows before the page plus the rows on it, or null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">More rows came back than the page holds.</exception>
    public static int? TotalFrom(this Page page, int returned, int? atMost = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegative(returned);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(returned, page.Size);

        var reached = page.Skip + returned;

        // An empty page past the first is the exception: the collection ends somewhere before it,
        // and nothing on the page says where.
        if (returned < page.Size && (returned > 0 || page.Number == 1))
        {
            return reached;
        }

        // A full page that reaches the bound holds the last row there can be. An empty one past
        // the bound says only that there are no more than the bound, not how many.
        return returned > 0 && reached >= atMost ? reached : null;
    }

    /// <summary>
    /// The total for a page, counting the collection only when the page's rows do not settle it.
    /// </summary>
    /// <param name="page">The page asked for.</param>
    /// <param name="returned">How many rows it came back with.</param>
    /// <param name="count">Counts the whole collection: the query the page was taken from.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <param name="atMost">The most rows the query can match, when a key guarantees it (see <see cref="TotalFrom"/>).</param>
    /// <returns>How many there are across every page.</returns>
    public static async Task<int> TotalAsync(
        this Page page,
        int returned,
        Func<CancellationToken, Task<int>> count,
        CancellationToken cancellationToken = default,
        int? atMost = null)
    {
        ArgumentNullException.ThrowIfNull(count);

        return page.TotalFrom(returned, atMost) ?? await count(cancellationToken);
    }

    /// <summary>
    /// Publishes the rows <see cref="Page{TEntity}"/>'s query returned, with the total, counting
    /// the collection only when the rows do not settle it.
    /// </summary>
    /// <typeparam name="TRow">The row read.</typeparam>
    /// <typeparam name="TResource">What the page publishes.</typeparam>
    /// <param name="rows">The rows the page's query returned.</param>
    /// <param name="page">The page asked for.</param>
    /// <param name="map">Turns a row into what the page publishes.</param>
    /// <param name="count">Counts the whole collection: the query the page was taken from.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    /// <param name="atMost">The most rows the query can match, when a key guarantees it (see <see cref="TotalFrom"/>).</param>
    /// <returns>The page and its total.</returns>
    public static async Task<Paged<TResource>> ToPagedAsync<TRow, TResource>(
        this IReadOnlyList<TRow> rows,
        Page page,
        Func<TRow, TResource> map,
        Func<CancellationToken, Task<int>> count,
        CancellationToken cancellationToken = default,
        int? atMost = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(map);

        var total = await page.TotalAsync(rows.Count, count, cancellationToken, atMost);

        return new Paged<TResource>([.. rows.Select(map)], total);
    }

    /// <summary>
    /// Takes a page from a collection already in memory, whose size is known without counting.
    /// </summary>
    /// <typeparam name="T">The resource.</typeparam>
    /// <param name="all">The whole collection, in the order it pages in.</param>
    /// <param name="page">The page asked for.</param>
    /// <returns>The page and its total.</returns>
    public static Paged<T> ToPaged<T>(this IReadOnlyCollection<T> all, Page page)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(page);

        return new Paged<T>([.. all.Skip(page.Skip).Take(page.Size)], all.Count);
    }
}
