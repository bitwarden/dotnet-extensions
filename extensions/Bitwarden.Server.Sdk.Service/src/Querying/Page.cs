namespace Bitwarden.Server.Sdk.Service.Querying;

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
/// </remarks>
public sealed record Page
{
    private readonly int _number = 1;
    private readonly int _size = DefaultSize;

    /// <summary>
    /// How many resources a page holds when a caller does not say.
    /// </summary>
    public const int DefaultSize = 50;

    /// <summary>
    /// The most a caller may ask for at once.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than an error: a caller asking for more is given this, because failing
    /// a read for being too ambitious helps nobody. The limit exists so one request cannot ask
    /// a service to materialise an unbounded result.
    /// </remarks>
    public const int MaximumSize = 200;

    /// <summary>
    /// The farthest page a caller may ask for: the last whose rows, at the largest size, can still
    /// be counted in an <see cref="int"/>.
    /// </summary>
    /// <remarks>
    /// Far past any collection a caller pages through by number, and what keeps <see cref="Skip"/>
    /// and the rows a page reaches from overflowing into a negative offset.
    /// </remarks>
    internal const int MaximumNumber = int.MaxValue / MaximumSize;

    /// <summary>
    /// The page being asked for, from one.
    /// </summary>
    /// <exception cref="InvalidQueryException">The number is less than one, or past the farthest page.</exception>
    public int Number
    {
        get => _number;

        // Refused rather than quietly replaced by the first page. A caller who asked for page
        // zero has made a mistake, and answering with a page they did not ask for hides it.
        init => _number = value is >= 1 and <= MaximumNumber
            ? value
            : throw PastTheLast(value.ToString(), value >= 1);
    }

    /// <summary>
    /// Refuses a page number, saying how far a caller may go when that is what was wrong.
    /// </summary>
    internal static InvalidQueryException PastTheLast(string value, bool tooFar) =>
        tooFar
            ? new InvalidQueryException(PageNumber.Parameter, value, $"must be a whole number from 1 to {MaximumNumber}")
            : new InvalidQueryException(PageNumber.Parameter, value);

    /// <summary>
    /// How many resources the page holds.
    /// </summary>
    /// <exception cref="InvalidQueryException">The size is less than one.</exception>
    public int Size
    {
        get => _size;

        init => _size = value >= 1
            ? Math.Min(value, MaximumSize)
            : throw new InvalidQueryException("page[size]", value.ToString());
    }

    /// <summary>
    /// How many resources to skip to reach this page.
    /// </summary>
    public int Skip => (Number - 1) * Size;
}
