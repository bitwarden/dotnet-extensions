using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// One field of a sort, written as the <c>sort</c> parameter writes it: a leading minus for
/// descending.
/// </summary>
public sealed class SortByTests
{
    [Fact]
    public void Ascending_IsWrittenAsTheFieldAlone()
    {
        var actual = SortBy.Ascending("name");

        Assert.False(actual.IsDescending);
        Assert.Equal(SortDirection.Ascending, actual.Direction);
        Assert.Equal("name", actual.ToString());
    }

    [Fact]
    public void Descending_IsWrittenWithALeadingMinus()
    {
        var actual = SortBy.Descending("createdAt");

        Assert.True(actual.IsDescending);
        Assert.Equal("-createdAt", actual.ToString());
    }

    [Fact]
    public void Equality_IsByFieldAndDirection()
    {
        Assert.Equal(SortBy.Ascending("name"), new SortBy("name", SortDirection.Ascending));
        Assert.NotEqual(SortBy.Ascending("name"), SortBy.Descending("name"));
    }
}
