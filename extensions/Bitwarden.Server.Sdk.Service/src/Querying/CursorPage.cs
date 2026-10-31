namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Which slice of a collection a caller asked for, by position rather than by number.
/// </summary>
/// <remarks>
/// <para>
/// The standards' other paging style, for collections too large or too fast-moving for
/// <see cref="Page"/>. An offset page costs a count of everything that matches and a skip over
/// everything before it, both proportional to where the caller has got to, on every request,
/// and rows inserted while a caller pages shift everything after them, so a page is repeated or
/// missed. A cursor names the last row the caller saw, and the next page is "what comes after
/// it": one index seek, however deep, and stable however much is written meanwhile.
/// </para>
/// <para>
/// What it gives up is the total and the ability to jump to page forty. For an audit log read a
/// month at a time neither was ever wanted, and that is the kind of collection this is for.
/// </para>
/// <para>
/// Declared as two parameters, as <see cref="Page"/> is:
/// </para>
/// <code>
/// public async Task&lt;CursorPagedResourceListDocument&lt;Event&gt;&gt; ListEvents(
///     PageSize pageSize = default,
///     PageAfter pageAfter = default,
///     CancellationToken cancellationToken = default)
/// {
///     var page = new CursorPage { Size = pageSize, After = pageAfter };
/// </code>
/// </remarks>
public sealed record CursorPage
{
    private readonly int _size = Page.DefaultSize;

    /// <summary>
    /// How many resources the page holds.
    /// </summary>
    /// <remarks>
    /// The same rules as <see cref="Page.Size"/>: a ceiling of <see cref="Page.MaximumSize"/>, and nothing below one.
    /// </remarks>
    /// <exception cref="InvalidQueryException">The size is less than one.</exception>
    public int Size
    {
        get => _size;

        init => _size = value >= 1
            ? Math.Min(value, Page.MaximumSize)
            : throw new InvalidQueryException("page[size]", value.ToString());
    }

    /// <summary>
    /// The cursor the previous page handed out, or null for the first page.
    /// </summary>
    /// <remarks>
    /// Opaque. A caller passes back what it was given and nothing else; the service may change
    /// what is inside at any time, and one that has been altered is refused.
    /// </remarks>
    public string? After { get; init; }
}
