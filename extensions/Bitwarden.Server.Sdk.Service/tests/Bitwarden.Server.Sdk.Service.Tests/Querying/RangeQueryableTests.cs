using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A query narrowed to a range keeps exactly the rows the range contains, each end as inclusive as
/// the range says, and no range leaves the query as it was.
/// </summary>
public sealed class RangeQueryableTests
{
    private static IQueryable<Row> Rows => Enumerable.Range(1, 5).Select(count => new Row { Count = count }).AsQueryable();

    [Fact]
    public void Within_NoRange_IsTheQueryUnchanged()
    {
        var source = Rows;

        Assert.Same(source, source.Within(row => row.Count, null));
    }

    [Theory]
    [InlineData(true, true, new[] { 2, 3, 4 })]
    [InlineData(false, false, new[] { 3 })]
    [InlineData(true, false, new[] { 2, 3 })]
    [InlineData(false, true, new[] { 3, 4 })]
    public void Within_BothEnds_IsAsInclusiveAsEachSays(bool lowerInclusive, bool upperInclusive, int[] expected)
    {
        var actual = Rows.Within(row => row.Count, new Range<int>(2, lowerInclusive, 4, upperInclusive)).Select(row => row.Count);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Within_LowerOnly_HasNoUpperLimit()
    {
        var actual = Rows.Within(row => row.Count, new Range<int>(4, true, null, false)).Select(row => row.Count);

        Assert.Equal([4, 5], actual);
    }

    [Fact]
    public void Within_UpperOnly_HasNoLowerLimit()
    {
        var actual = Rows.Within(row => row.Count, new Range<int>(null, false, 2, false)).Select(row => row.Count);

        Assert.Equal([1], actual);
    }

    // The bound travels as a captured value, as a provider parameterizes it, not inlined in the query.
    [Fact]
    public void Within_Bound_IsCapturedNotInlined()
    {
        var actual = Rows.Within(row => row.Count, new Range<int>(2, true, null, false)).Expression.ToString();

        Assert.DoesNotContain(">= 2", actual, StringComparison.Ordinal);
    }

    [Fact]
    public void Within_NoSourceOrField_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IQueryable<Row>)null!).Within(row => row.Count, null));
        Assert.Throws<ArgumentNullException>(() => Rows.Within<Row, int>(null!, null));
    }

    private sealed class Row
    {
        public int Count { get; init; }
    }
}
