namespace Bitwarden.Server.Sdk.Resources.Querying;

/// <summary>
/// Which slice of a collection a caller asked for.
/// </summary>
/// <remarks>
/// <para>
/// A value, not a bound parameter. An operation that pages declares the two query parameters the
/// standards name as two ordinary parameters of its own, typed <see cref="PageNumber"/> and
/// <see cref="PageSize"/>:
/// </para>
/// <code>
/// public async Task&lt;PagedResourceListDocument&lt;User&gt;&gt; ListUsers(
///     PageNumber pageNumber = default,
///     PageSize pageSize = default,
///     CancellationToken cancellationToken = default)
/// {
///     var page = new Page { Number = pageNumber, Size = pageSize };
/// </code>
/// <para>
/// One query parameter, one method parameter, no exceptions, so a reader of the signature can
/// see the whole request without knowing anything about the platform. The types carry the
/// bracketed names the standards give the parameters, so no operation spells them out.
/// </para>
/// <para>
/// Constructing this is what applies the rules, so an operation cannot accept a page the
/// standards do not allow by forgetting to check. Numbered from one, because a caller reading
/// <c>page[number]=1</c> in a URL means the first page; offsets are the service's arithmetic,
/// not the caller's.
/// </para>
/// <para>
/// One of the standards' two paging styles, and the one for a collection a caller browses (a
/// member list, a set of policies) where a total and a jump to page four are wanted and the
/// collection is small enough that counting it costs nothing. A collection that is large, read
/// in order, and written to while it is read, such as an event log, pages by <see cref="CursorPage"/>
/// instead, which never counts and never shifts.
/// </para>
/// <para>
/// An operation that declares a maximum of its own with <see cref="MaximumPageSizeAttribute"/> builds
/// its page with <see cref="Page(PageNumber, PageSize)"/>, which carries that maximum; one built with
/// an initializer alone holds at most <see cref="MaximumSize"/>:
/// </para>
/// <code>
/// [MaximumPageSize(1000)] PageSize pageSize = default,
/// ...
/// var page = new Page(pageNumber, pageSize);
/// </code>
/// </remarks>
public sealed record Page
{
    private readonly int _number = 1;
    private readonly int _size = DefaultSize;

    /// <summary>
    /// The first page, of <see cref="DefaultSize"/>, holding at most <see cref="MaximumSize"/>.
    /// </summary>
    public Page()
    {
    }

    /// <summary>
    /// The page a caller asked for, holding at most the maximum the operation declares.
    /// </summary>
    /// <param name="number">The page, from one.</param>
    /// <param name="size">How many it holds, with the operation's maximum (<see cref="PageSize.Maximum"/>).</param>
    /// <exception cref="InvalidQueryException">The number is less than one or past the farthest page, or the size is less than one.</exception>
    public Page(PageNumber number, PageSize size)
    {
        SizeLimit = size.Maximum;
        Number = number;
        Size = size;
    }

    /// <summary>
    /// How many resources a page holds when a caller does not say.
    /// </summary>
    public const int DefaultSize = 50;

    /// <summary>
    /// The most a caller may ask for at once, unless the operation declares a maximum of its own
    /// with <see cref="MaximumPageSizeAttribute"/>.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than an error: a caller asking for more is given this, because failing
    /// a read for being too ambitious helps nobody. The limit exists so one request cannot ask
    /// a service to materialise an unbounded result.
    /// </remarks>
    public const int MaximumSize = 200;

    /// <summary>
    /// The farthest page a caller may ask for at <see cref="MaximumSize"/>.
    /// </summary>
    internal const int MaximumNumber = int.MaxValue / MaximumSize;

    /// <summary>
    /// The most this page may hold: <see cref="MaximumSize"/>, or the maximum the operation declares
    /// when the page is built with <see cref="Page(PageNumber, PageSize)"/>.
    /// </summary>
    public int SizeLimit { get; } = MaximumSize;

    /// <summary>
    /// The farthest page a caller may ask for when a page holds up to <paramref name="sizeLimit"/>:
    /// the last whose rows, at that size, can still be counted in an <see cref="int"/>.
    /// </summary>
    /// <remarks>
    /// Far past any collection a caller pages through by number, and what keeps <see cref="Skip"/>
    /// and the rows a page reaches from overflowing into a negative offset.
    /// </remarks>
    internal static int FarthestNumber(int sizeLimit) => int.MaxValue / sizeLimit;

    /// <summary>
    /// The page being asked for, from one.
    /// </summary>
    /// <exception cref="InvalidQueryException">The number is less than one, or past the farthest page.</exception>
    public int Number
    {
        get => _number;

        // Refused rather than quietly replaced by the first page. A caller who asked for page
        // zero has made a mistake, and answering with a page they did not ask for hides it.
        init => _number = value >= 1 && value <= FarthestNumber(SizeLimit)
            ? value
            : throw PastTheLast(value.ToString(), SizeLimit, value >= 1);
    }

    /// <summary>
    /// Refuses a page number, saying how far a caller may go when that is what was wrong.
    /// </summary>
    internal static InvalidQueryException PastTheLast(string value, int sizeLimit, bool tooFar) =>
        tooFar
            ? new InvalidQueryException(PageNumber.Parameter, value, $"must be a whole number from 1 to {FarthestNumber(sizeLimit)}")
            : new InvalidQueryException(PageNumber.Parameter, value);

    /// <summary>
    /// How many resources the page holds: at most <see cref="SizeLimit"/>.
    /// </summary>
    /// <exception cref="InvalidQueryException">The size is less than one.</exception>
    public int Size
    {
        get => _size;

        init => _size = value >= 1
            ? Math.Min(value, SizeLimit)
            : throw new InvalidQueryException("page[size]", value.ToString());
    }

    /// <summary>
    /// How many resources to skip to reach this page.
    /// </summary>
    public int Skip => (Number - 1) * Size;
}
