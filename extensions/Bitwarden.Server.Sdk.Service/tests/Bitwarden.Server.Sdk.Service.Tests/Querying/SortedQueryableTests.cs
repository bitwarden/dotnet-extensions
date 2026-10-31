using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A query ordered by a sort's fields in order of precedence, each in its own direction, with an
/// optional unique field last to break ties; and a field with nowhere to go is a wiring error.
/// </summary>
public sealed class SortedQueryableTests
{
    private static IQueryable<Row> Rows =>
        new[]
        {
            new Row { Id = 1, Name = "b", Count = 1 },
            new Row { Id = 2, Name = "a", Count = 2 },
            new Row { Id = 3, Name = "b", Count = 3 },
            new Row { Id = 4, Name = "a", Count = 2 },
        }.AsQueryable();

    [Theory]
    [InlineData("name", "count", new[] { 2, 4, 1, 3 })]
    [InlineData("name", "-count", new[] { 2, 4, 3, 1 })]
    [InlineData("-name", "count", new[] { 1, 3, 2, 4 })]
    [InlineData("-name", "-count", new[] { 3, 1, 2, 4 })]
    public void SortedBy_TwoFields_FirstTakesPrecedenceEachInItsDirection(string first, string second, int[] expected)
    {
        var actual = Rows.SortedBy(Sort.By(Parse(first), Parse(second))).ThenBy(row => row.Id).Select(row => row.Id);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SortedBy_TieBreak_OrdersRowsSharingEveryValue()
    {
        var actual = Rows.SortedBy(Sort.By(SortBy.Descending("count")), row => row.Id).Select(row => row.Id);

        Assert.Equal([3, 2, 4, 1], actual);
    }

    [Fact]
    public void SortedBy_SortForAResource_FieldTheResourceDoesNotSortBy_Throws()
    {
        var sort = Sort.For(typeof(Item), [SortBy.Ascending("count")]);

        var actual = Assert.Throws<InvalidOperationException>(() => Rows.SortedBy(sort));

        Assert.Contains("'count' is not a sortable field", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SortedBy_SortWithNoFields_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Rows.SortedBy(Sort.For(typeof(Item), [])));
    }

    [Fact]
    public void SortedBy_SortBuiltByHand_PropertyTheEntityDoesNotHave_Throws()
    {
        var actual = Assert.Throws<InvalidOperationException>(() => Rows.SortedBy(Sort.By(SortBy.Ascending("nope"))));

        Assert.Contains("'Row' has no property 'nope'", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SortedBy_NoSourceSortOrTieBreak_Throws()
    {
        var sort = Sort.By(SortBy.Ascending("name"));

        Assert.Throws<ArgumentNullException>(() => ((IQueryable<Row>)null!).SortedBy(sort));
        Assert.Throws<ArgumentNullException>(() => Rows.SortedBy(null!));
        Assert.Throws<ArgumentNullException>(() => Rows.SortedBy<Row, int>(sort, null!));
    }

    private static SortBy Parse(string field) =>
        field.StartsWith('-') ? SortBy.Descending(field[1..]) : SortBy.Ascending(field);

    private sealed class Row
    {
        public int Id { get; init; }

        public string Name { get; init; } = "";

        public int Count { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }

    [Sortable(nameof(CreatedAt), Default = true)]
    private sealed record Item : Resource
    {
        [Sortable]
        public string? Name { get; init; }
    }
}
