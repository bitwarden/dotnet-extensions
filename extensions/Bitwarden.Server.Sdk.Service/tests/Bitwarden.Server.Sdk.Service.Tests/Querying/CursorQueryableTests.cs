using System.Text;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A cursor continues exactly where the page it came with ended, in the order it was issued for,
/// and anything else sent as one is refused as the caller's mistake.
/// </summary>
public sealed class CursorQueryableTests
{
    private static readonly Sort ByName = Sort.By(SortBy.Ascending("Name"));

    // Two rows share a name, so the tie-break decides where the first page ends.
    private static readonly Row[] Collection =
    [
        new() { Id = 5, Name = "c" },
        new() { Id = 1, Name = "a" },
        new() { Id = 3, Name = "b" },
        new() { Id = 2, Name = "b" },
        new() { Id = 4, Name = "d" },
    ];

    [Fact]
    public void ToCursorPage_WalkedToTheEnd_ReturnsEveryRowOnceInOrder()
    {
        var seen = new List<int>();
        string? after = null;
        var pages = 0;

        do
        {
            var page = new CursorPage { Size = 2, After = after };
            var rows = Collection.AsQueryable().Page(page, ByName, row => row.Id).ToList();
            var paged = rows.ToCursorPage(page, ByName, row => row.Id, row => row.Id);

            seen.AddRange(paged.Items);
            after = paged.Cursor;
            pages++;
        }
        while (after is not null);

        Assert.Equal([1, 2, 3, 5, 4], seen);
        Assert.Equal(3, pages);
    }

    [Fact]
    public void ToCursorPage_LastPage_HasNoCursor()
    {
        var page = new CursorPage { Size = Collection.Length };
        var rows = Collection.AsQueryable().Page(page, ByName, row => row.Id).ToList();

        Assert.False(rows.ToCursorPage(page, ByName, row => row.Id, row => row.Id).HasMore);
    }

    [Fact]
    public void Page_CursorIssuedForAnotherSort_IsRefused()
    {
        var cursor = FirstCursor();

        var actual = Assert.Throws<InvalidCursorException>(() =>
            Collection.AsQueryable().Page(new CursorPage { Size = 2, After = cursor }, Sort.By(SortBy.Descending("Name")), row => row.Id).ToList());

        Assert.Contains("different sort", actual.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a cursor!")]
    [InlineData("e30")]
    [InlineData("W10")]
    public void Page_MalformedCursor_IsRefused(string cursor)
    {
        Assert.Throws<InvalidCursorException>(() => Next(cursor));
    }

    [Theory]
    [InlineData("""{"s":5,"k":["a",1]}""")]
    [InlineData("""{"s":{"x":1},"k":["a",1]}""")]
    [InlineData("""{"s":["Name"],"k":["a",1]}""")]
    [InlineData("""{"s":"Name","k":["a"]}""")]
    [InlineData("""{"s":"Name","k":[5,1]}""")]
    [InlineData("""{"s":"Name","k":["a","one"]}""")]
    [InlineData("""{"s":"Name","k":["a",null]}""")]
    public void Page_TamperedCursor_IsRefused(string json)
    {
        Assert.Throws<InvalidCursorException>(() => Next(Encode(json)));
    }

    [Fact]
    public void Page_CursorEditedToSkipAhead_ContinuesFromWhereItSays()
    {
        // A cursor is not a secret: one a caller writes by hand pages its own rows from there.
        Assert.Equal([5, 4], Next(Encode("""{"s":"Name","k":["b",3]}""")).Select(row => row.Id));
    }

    private static List<Row> Next(string cursor) =>
        Collection.AsQueryable().Page(new CursorPage { Size = 2, After = cursor }, ByName, row => row.Id).ToList();

    private static string FirstCursor()
    {
        var page = new CursorPage { Size = 2 };
        var rows = Collection.AsQueryable().Page(page, ByName, row => row.Id).ToList();
        return rows.ToCursorPage(page, ByName, row => row.Id, row => row.Id).Cursor!;
    }

    private static string Encode(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class Row
    {
        public int Id { get; init; }

        public string Name { get; init; } = "";
    }
}
