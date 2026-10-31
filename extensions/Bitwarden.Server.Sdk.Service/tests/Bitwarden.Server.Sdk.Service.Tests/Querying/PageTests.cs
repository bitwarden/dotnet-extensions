using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A page the standards allow, and nothing else: numbered from one, no bigger than the maximum,
/// and never so far in that where it starts cannot be counted.
/// </summary>
public sealed class PageTests
{
    [Fact]
    public void Size_MoreThanTheMaximum_IsTheMaximum()
    {
        Assert.Equal(Page.MaximumSize, new Page { Size = Page.MaximumSize + 1 }.Size);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Size_LessThanOne_IsRefused(int size)
    {
        Assert.Throws<InvalidQueryException>(() => new Page { Size = size });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Number_LessThanOne_IsRefused(int number)
    {
        Assert.Throws<InvalidQueryException>(() => new Page { Number = number });
    }

    // The farthest page whose rows, however many it holds, can still be counted to.
    [Fact]
    public void Skip_FarthestPage_IsCountable()
    {
        var page = new Page { Number = Page.MaximumNumber, Size = Page.MaximumSize };

        Assert.Equal((long)(Page.MaximumNumber - 1) * Page.MaximumSize, page.Skip);
        Assert.Null(page.TotalFrom(Page.MaximumSize));
        Assert.Equal(page.Skip + 1, page.TotalFrom(1));
    }

    // Past it, the rows before the page are more than a count can hold.
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(Page.MaximumNumber + 1)]
    public void Number_PastTheFarthestPage_IsRefused(int number)
    {
        var actual = Assert.Throws<InvalidQueryException>(() => new Page { Number = number, Size = Page.MaximumSize });

        Assert.Equal("page[number]", Assert.Single(actual.Errors).Source?.Parameter);
        Assert.Contains($"from 1 to {Page.MaximumNumber}", Assert.Single(actual.Errors).Detail, StringComparison.Ordinal);
    }
}
