using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

// The standards' notation, read exactly: a comma separates values unless it is escaped, and a
// range is two bounds between brackets that say whether each end is included.
public sealed class FilterSyntaxTests
{
    [Theory]
    [InlineData("a,b,c", new[] { "a", "b", "c" })]
    [InlineData(@"Smith\, Jr", new[] { "Smith, Jr" })]
    [InlineData(@"a\\,b", new[] { @"a\", "b" })]
    [InlineData(" a , b ", new[] { "a", "b" })]
    public void TrySplit_HonoursEscapes(string value, string[] expected)
    {
        Assert.True(FilterSyntax.TrySplit(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TrySplit_TrailingLoneBackslash_IsRefused()
    {
        Assert.False(FilterSyntax.TrySplit(@"abc\", out _));
    }

    [Theory]
    [InlineData("[1,5]", "1", true, "5", true)]
    [InlineData("(1,5)", "1", false, "5", false)]
    [InlineData("[3,*)", "3", true, null, false)]
    [InlineData("(*,2000-01-01T00:00:00Z)", null, false, "2000-01-01T00:00:00Z", false)]
    public void TryRange_ReadsBoundsAndInclusivity(
        string value,
        string? lower,
        bool lowerInclusive,
        string? upper,
        bool upperInclusive)
    {
        Assert.True(FilterSyntax.TryRange(value, out var actual));
        Assert.Equal((lower, lowerInclusive, upper, upperInclusive), actual);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("[1,5")]
    [InlineData("[1]")]
    [InlineData("[1,2,3]")]
    [InlineData("[,5]")]
    [InlineData("")]
    public void TryRange_NotARange_IsRefused(string value)
    {
        Assert.False(FilterSyntax.TryRange(value, out _));
    }
}
