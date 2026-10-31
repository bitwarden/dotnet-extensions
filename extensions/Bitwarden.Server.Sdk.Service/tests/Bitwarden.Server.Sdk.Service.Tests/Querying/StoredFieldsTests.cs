using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// Where a resource's sortable and filterable fields are stored, and what happens when nobody said.
/// </summary>
public sealed class StoredFieldsTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static IQueryable<Row> Rows =>
        new[] { new Row { OwnerId = Second, Name = "b" }, new Row { OwnerId = First, Name = "a" } }.AsQueryable();

    // Published as "owner", stored as OwnerId: the sort goes through MappedTo.
    [Fact]
    public void SortedBy_FieldMappedToAnotherProperty_SortsByThatProperty()
    {
        var actual = Rows.SortedBy(Sort.For(typeof(Mapped), [SortBy.Ascending("owner")])).Select(row => row.OwnerId);

        Assert.Equal([First, Second], actual);
    }

    // The convention, for the fields that need nothing: same name, any case.
    [Fact]
    public void SortedBy_FieldNamedLikeItsProperty_SortsByIt()
    {
        var actual = Rows.SortedBy(Sort.For(typeof(Mapped), [SortBy.Descending("name")])).Select(row => row.Name);

        Assert.Equal(["b", "a"], actual);
    }

    // The failure this exists for, caught on the first sort, with every missing field named at once.
    [Fact]
    public void SortedBy_SortableFieldWithNowhereToGo_FailsNamingIt()
    {
        var actual = Assert.Throws<InvalidOperationException>(() =>
            Rows.SortedBy(Sort.For(typeof(Unmapped), [SortBy.Ascending("name")])).ToList());

        Assert.Contains("'owner' (looked for 'Row.Owner')", actual.Message, StringComparison.Ordinal);
        Assert.Contains("MappedTo", actual.Message, StringComparison.Ordinal);
    }

    // A sort built by hand names the entity's own properties.
    [Fact]
    public void SortedBy_SortBuiltByHand_NamesTheEntitysProperty()
    {
        var actual = Rows.SortedBy(Sort.By(SortBy.Ascending("ownerId"))).Select(row => row.OwnerId);

        Assert.Equal([First, Second], actual);
    }

    [Fact]
    public void Matching_FieldMappedToAnotherProperty_FiltersByThatProperty()
    {
        var query = Query($$"""{ "==": [{ "var": "owner" }, "{{First}}"] }""");
        ResourceFields.Require(query, typeof(Mapped));

        var actual = Rows.Matching(query).Select(row => row.Name);

        Assert.Equal(["a"], actual);
    }

    // An expression may name only what the resource marks filterable.
    [Fact]
    public void Require_FieldNotMarkedFilterable_Refused()
    {
        var query = Query("""{ "==": [{ "var": "name" }, "a"] }""");

        var actual = Assert.Throws<Common.InvalidRequestException>(() => ResourceFields.Require(query, typeof(Mapped)));

        Assert.Contains("'name' is not a field this resource may be filtered on", actual.Message, StringComparison.Ordinal);
    }

    private static Query Query(string filter) => JsonSerializer.Deserialize<Query>(filter)!;

    private sealed class Row
    {
        public Guid OwnerId { get; set; }

        public string Name { get; set; } = "";

        public DateTimeOffset CreatedAt { get; set; }
    }

    [Sortable(nameof(CreatedAt), Default = true)]
    private sealed record Mapped : Resource
    {
        [Sortable(MappedTo = nameof(Row.OwnerId))]
        [Filterable(MappedTo = nameof(Row.OwnerId))]
        public Guid Owner { get; init; }

        [Sortable]
        public string? Name { get; init; }
    }

    [Sortable(nameof(CreatedAt), Default = true)]
    private sealed record Unmapped : Resource
    {
        [Sortable]
        public Guid Owner { get; init; }

        [Sortable]
        public string? Name { get; init; }
    }
}
