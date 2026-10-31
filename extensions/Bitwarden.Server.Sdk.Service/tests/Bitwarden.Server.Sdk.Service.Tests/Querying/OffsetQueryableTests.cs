using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// Offset paging counts the collection only when the page's own rows cannot say how big it is.
/// </summary>
public sealed class OffsetQueryableTests
{
    private static readonly int[] Collection = [.. Enumerable.Range(1, 23)];

    [Fact]
    public void TotalFrom_FirstPageNotFull_IsTheRowsOnIt()
    {
        Assert.Equal(3, new Page { Number = 1, Size = 10 }.TotalFrom(3));
    }

    [Fact]
    public void TotalFrom_EmptyFirstPage_IsZero()
    {
        Assert.Equal(0, new Page { Number = 1, Size = 10 }.TotalFrom(0));
    }

    [Fact]
    public void TotalFrom_LaterPageNotFull_IsTheRowsBeforeItPlusTheRowsOnIt()
    {
        Assert.Equal(23, new Page { Number = 3, Size = 10 }.TotalFrom(3));
    }

    [Fact]
    public void TotalFrom_FullPage_IsUnknown()
    {
        Assert.Null(new Page { Number = 2, Size = 10 }.TotalFrom(10));
    }

    [Fact]
    public void TotalFrom_EmptyPagePastTheFirst_IsUnknown()
    {
        Assert.Null(new Page { Number = 4, Size = 10 }.TotalFrom(0));
    }

    [Fact]
    public void TotalFrom_MoreRowsThanThePageHolds_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Page { Number = 1, Size = 10 }.TotalFrom(11));
    }

    [Fact]
    public void TotalFrom_FullPageReachingTheBound_IsTheBound()
    {
        // page[size]=1 asking for the one entitlement an organization can have.
        Assert.Equal(1, new Page { Number = 1, Size = 1 }.TotalFrom(1, atMost: 1));
    }

    [Fact]
    public void TotalFrom_FullPageShortOfTheBound_IsUnknown()
    {
        Assert.Null(new Page { Number = 1, Size = 2 }.TotalFrom(2, atMost: 3));
    }

    [Fact]
    public void TotalFrom_EmptyPagePastTheBound_IsUnknown()
    {
        // No more than three, but nothing says whether there are one, two or three.
        Assert.Null(new Page { Number = 4, Size = 1 }.TotalFrom(0, atMost: 3));
    }

    [Fact]
    public async Task ToPagedAsync_FullPageReachingTheBound_DoesNotCount()
    {
        var page = new Page { Number = 1, Size = 2 };

        var paged = await new[] { 7, 9 }.ToPagedAsync(
            page,
            row => row,
            _ => throw new InvalidOperationException("Counted a page that reached its bound."),
            TestContext.Current.CancellationToken,
            atMost: 2);

        Assert.Equal(2, paged.Total);
    }

    [Theory]
    [InlineData(1, 50, false)] // fits on its first page
    [InlineData(3, 10, false)] // the last page, not full
    [InlineData(1, 10, true)] // full: more may follow
    [InlineData(2, 10, true)] // full
    [InlineData(4, 10, true)] // empty, past the end
    [InlineData(9, 10, true)] // empty, well past the end
    public async Task ToPagedAsync_CountsOnlyWhenThePageCannotSay(int number, int size, bool counts)
    {
        var page = new Page { Number = number, Size = size };
        var counted = 0;
        var rows = Collection.AsQueryable().Page(page).ToList();

        var paged = await rows.ToPagedAsync(
            page,
            row => $"item {row}",
            _ =>
            {
                counted++;
                return Task.FromResult(Collection.Length);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(counts ? 1 : 0, counted);
        Assert.Equal(Collection.Length, paged.Total);
        Assert.Equal(rows.Select(row => $"item {row}"), paged.Items);
    }

    [Fact]
    public void Page_TakesTheSliceAfterTheSkip()
    {
        Assert.Equal([11, 12, 13, 14, 15], Collection.AsQueryable().Page(new Page { Number = 3, Size = 5 }));
    }

    [Fact]
    public void ToPaged_InMemory_TakesThePageAndCountsWhatItHolds()
    {
        var paged = Collection.ToPaged(new Page { Number = 3, Size = 10 });

        Assert.Equal([21, 22, 23], paged.Items);
        Assert.Equal(23, paged.Total);
    }
}
