using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A range always describes some values: its bounds are in order, equal bounds include
/// themselves, an open end includes nothing, and each end is respected when testing a value.
/// </summary>
public sealed class RangeTests
{
    [Fact]
    public void New_Inverted_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new Range<int>(5, true, 1, true));
    }

    // Equal bounds are a range of one value only when both ends include it. Otherwise nothing
    // could match, which a caller cannot have meant.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void New_EqualBoundsWithAnExclusiveEnd_IsRefused(bool lowerInclusive, bool upperInclusive)
    {
        Assert.Throws<ArgumentException>(() => new Range<int>(3, lowerInclusive, 3, upperInclusive));
    }

    [Fact]
    public void New_EqualBoundsBothIncluded_IsThatOneValue()
    {
        var actual = new Range<int>(3, true, 3, true);

        Assert.True(actual.Contains(3));
        Assert.False(actual.Contains(2));
        Assert.False(actual.Contains(4));
    }

    // An end with no bound cannot include it, whatever the caller said.
    [Fact]
    public void New_OpenEnd_IsNeverInclusive()
    {
        var actual = new Range<int>(null, true, null, true);

        Assert.False(actual.LowerInclusive);
        Assert.False(actual.UpperInclusive);
    }

    [Fact]
    public void Contains_RespectsEachEnd()
    {
        var actual = new Range<int>(1, false, 5, true);

        Assert.False(actual.Contains(1));
        Assert.True(actual.Contains(2));
        Assert.True(actual.Contains(5));
        Assert.False(actual.Contains(6));
    }

    [Fact]
    public void Contains_InclusiveLowerAndExclusiveUpper()
    {
        var actual = new Range<int>(1, true, 5, false);

        Assert.True(actual.Contains(1));
        Assert.False(actual.Contains(5));
    }

    [Fact]
    public void Contains_Unbounded_IsEverything()
    {
        var actual = new Range<int>(null, false, null, false);

        Assert.True(actual.Contains(int.MinValue));
        Assert.True(actual.Contains(int.MaxValue));
    }
}
