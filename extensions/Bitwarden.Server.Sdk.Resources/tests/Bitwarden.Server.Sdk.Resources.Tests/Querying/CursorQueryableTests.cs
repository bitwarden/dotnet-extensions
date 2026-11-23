using System.Linq.Expressions;
using System.Text;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Tests.Querying;

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
        new() { Id = 3, Name = "b", Shade = Shade.DarkBlue },
        new() { Id = 2, Name = "b" },
        new() { Id = 4, Name = "d", Shade = Shade.DarkBlue },
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
    [InlineData("""{"s":"Name","s":"Name","k":["a",1]}""")]
    [InlineData("""{"s":"\uD800","k":["a",1]}""")]
    [InlineData("""{"s":"Name","k":["\uD800",1]}""")]
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

    // Providers disagree about where nulls sort, so the same cursor would mean different rows.
    [Fact]
    public void Page_NullableSortField_IsAWiringError()
    {
        var actual = Assert.Throws<InvalidOperationException>(() =>
            Collection.AsQueryable().Page(new CursorPage { Size = 2 }, Sort.By(SortBy.Ascending("Score")), row => row.Id).ToList());

        Assert.Contains("Row.Score can be null", actual.Message, StringComparison.Ordinal);
    }

    // Annotated as nullable, a reference type holds null as surely as a Nullable<T> does, and the
    // cursor minted from a row holding it names no place to continue from.
    [Fact]
    public void Page_NullableReferenceSortField_IsAWiringError()
    {
        var actual = Assert.Throws<InvalidOperationException>(() =>
            Collection.AsQueryable().Page(new CursorPage { Size = 2 }, Sort.By(SortBy.Ascending("Nickname")), row => row.Id).ToList());

        Assert.Contains("Row.Nickname can be null", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_NullableReferenceTieBreak_IsAWiringError()
    {
        var actual = Assert.Throws<InvalidOperationException>(() =>
            Collection.AsQueryable().Page(new CursorPage { Size = 2 }, ByName, row => row.Nickname).ToList());

        Assert.Contains("the tie-break of Row can be null", actual.Message, StringComparison.Ordinal);
    }

    // A key type no converter reads mints no cursor, so any cursor naming one was not issued here.
    [Fact]
    public void Page_CursorForAKeyWithNoConverter_IsRefused()
    {
        var page = new CursorPage { Size = 2, After = Encode("""{"s":"Handle","k":[1,1]}""") };

        Assert.Throws<InvalidCursorException>(() =>
            Collection.AsQueryable().Page(page, Sort.By(SortBy.Ascending("Handle")), row => row.Id).ToList());
    }

    // The cursor's values travel as captured values, as a provider parameterizes them, not
    // inlined in the query.
    [Fact]
    public void Page_CursorValues_AreCapturedNotInlined()
    {
        var constants = new Constants();
        constants.Visit(Collection.AsQueryable()
            .Page(new CursorPage { Size = 5, After = Encode("""{"s":"Name","k":["b",3]}""") }, ByName, row => row.Id)
            .Expression);

        Assert.DoesNotContain(constants.Found, constant => constant.Value is "b" or 3);
    }

    [Fact]
    public void Page_NullableTieBreak_IsAWiringError()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Collection.AsQueryable().Page(new CursorPage { Size = 2 }, ByName, row => (int?)row.Id).ToList());
    }

    [Fact]
    public void ToCursorPage_EnumerationSortField_ContinuesInItsOrder()
    {
        var byShade = Sort.By(SortBy.Descending("Shade"));
        var page = new CursorPage { Size = 2 };
        var first = Collection.AsQueryable().Page(page, byShade, row => row.Id).ToList()
            .ToCursorPage(page, byShade, row => row.Id, row => row.Id);

        var next = new CursorPage { Size = 10, After = first.Cursor };
        var rest = Collection.AsQueryable().Page(next, byShade, row => row.Id).Select(row => row.Id);

        Assert.Equal([3, 4], first.Items);
        Assert.Equal([1, 2, 5], rest);
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

        public int? Score { get; init; }

        public string? Nickname { get; init; }

        public nint Handle { get; init; }

        public Shade Shade { get; init; }
    }

    /// <summary>
    /// Every constant in an expression tree.
    /// </summary>
    private sealed class Constants : ExpressionVisitor
    {
        public List<ConstantExpression> Found { get; } = [];

        protected override Expression VisitConstant(ConstantExpression node)
        {
            Found.Add(node);
            return base.VisitConstant(node);
        }
    }

    private enum Shade
    {
        Light,
        DarkBlue,
    }
}
