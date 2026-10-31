namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A range a caller filtered by, bound from <c>filter[…]=[begin,end)</c>.
/// </summary>
/// <typeparam name="T">What the range spans: a date, a number.</typeparam>
/// <remarks>
/// <para>
/// Declared as the value of a filter, like any other:
/// </para>
/// <code>
/// public async Task&lt;PagedResourceListDocument&lt;User&gt;&gt; ListUsers(
///     Filter&lt;Range&lt;DateTimeOffset&gt;&gt;? createdAt,
///     …)
/// </code>
/// <para>
/// The standards' notation: <c>[</c> and <c>]</c> include the bound, <c>(</c> and <c>)</c>
/// exclude it, and <c>*</c> leaves that end open, so <c>[3,*)</c> is "three or more", and
/// <c>[2000-01-01T00:00:00Z,2001-01-01T00:00:00Z)</c> is the year 2000. Ranges are also how the
/// standards express less-than and greater-than, so there is no separate form for either.
/// </para>
/// <para>
/// Constructing one is what applies the rules: a range whose lower bound is above its upper, or
/// that could contain nothing at all, is refused rather than answered with nothing, because a
/// caller who sent one made a mistake and an empty result would hide it.
/// </para>
/// <para>
/// How much of a range is allowed (a year at most, say) is the operation's rule, read
/// from <see cref="Lower"/> and <see cref="Upper"/>. The platform only knows what a range is.
/// </para>
/// </remarks>
public sealed record Range<T> where T : struct, IComparable<T>
{
    /// <summary>
    /// The lower bound, or null when the range is open below.
    /// </summary>
    public T? Lower { get; }

    /// <summary>
    /// The upper bound, or null when the range is open above.
    /// </summary>
    public T? Upper { get; }

    /// <summary>
    /// Whether the lower bound itself is in the range.
    /// </summary>
    public bool LowerInclusive { get; }

    /// <summary>
    /// Whether the upper bound itself is in the range.
    /// </summary>
    public bool UpperInclusive { get; }

    /// <summary>
    /// Describes a range.
    /// </summary>
    /// <param name="lower">The lower bound, or null for none.</param>
    /// <param name="lowerInclusive">Whether the lower bound is in the range.</param>
    /// <param name="upper">The upper bound, or null for none.</param>
    /// <param name="upperInclusive">Whether the upper bound is in the range.</param>
    /// <exception cref="ArgumentException">The range is inverted, or cannot contain anything.</exception>
    public Range(T? lower, bool lowerInclusive, T? upper, bool upperInclusive)
    {
        if (lower is { } from && upper is { } to)
        {
            var order = from.CompareTo(to);

            if (order > 0)
            {
                throw new ArgumentException("The lower bound is above the upper bound.");
            }

            if (order == 0 && !(lowerInclusive && upperInclusive))
            {
                throw new ArgumentException("The bounds are equal and one excludes itself, so nothing is in range.");
            }
        }

        Lower = lower;
        Upper = upper;
        LowerInclusive = lower is not null && lowerInclusive;
        UpperInclusive = upper is not null && upperInclusive;
    }

    /// <summary>
    /// Whether a value falls within the range.
    /// </summary>
    /// <param name="value">The value.</param>
    public bool Contains(T value) =>
        (Lower is not { } lower || (LowerInclusive ? value.CompareTo(lower) >= 0 : value.CompareTo(lower) > 0))
        && (Upper is not { } upper || (UpperInclusive ? value.CompareTo(upper) <= 0 : value.CompareTo(upper) < 0));
}
