
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Tests.Querying;

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

    [Fact]
    public void Size_OperationsOwnMaximum_IsTheCeiling()
    {
        var page = new Page(new PageNumber(1), new PageSize(5000, 1000));

        Assert.Equal(1000, page.Size);
        Assert.Equal(1000, page.SizeLimit);
    }

    [Fact]
    public void Size_WithinTheOperationsOwnMaximum_IsKept()
    {
        Assert.Equal(700, new Page(new PageNumber(2), new PageSize(700, 1000)).Size);
    }

    [Fact]
    public void Size_OperationDeclaresNothing_IsTheDefaultMaximum()
    {
        var page = new Page(default, new PageSize(5000));

        Assert.Equal(Page.MaximumSize, page.Size);
        Assert.Equal(Page.MaximumSize, page.SizeLimit);
        Assert.Equal(Page.MaximumSize, new Page { Size = 5000 }.SizeLimit);
    }

    [Fact]
    public void Constructor_DefaultSize_IsTheDefault()
    {
        Assert.Equal(Page.DefaultSize, new Page(default, new PageSize(null, 1000)).Size);
    }

    // A larger page reaches its rows sooner, so the farthest page is nearer.
    [Fact]
    public void Number_PastTheFarthestPageAtTheOperationsMaximum_IsRefused()
    {
        var farthest = int.MaxValue / 1000;

        var page = new Page(new PageNumber(farthest), new PageSize(1000, 1000));
        var actual = Assert.Throws<InvalidQueryException>(() => new Page(new PageNumber(farthest + 1), new PageSize(1000, 1000)));

        Assert.Equal((long)(farthest - 1) * 1000, page.Skip);
        Assert.Contains($"from 1 to {farthest}", Assert.Single(actual.Errors).Detail, StringComparison.Ordinal);
    }
}
