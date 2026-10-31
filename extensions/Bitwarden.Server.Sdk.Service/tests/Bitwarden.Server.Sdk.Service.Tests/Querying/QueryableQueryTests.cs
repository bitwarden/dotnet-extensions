using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// An expression reaches only the fields its resource publishes, and one the platform cannot run
/// is refused as the caller's mistake rather than failing as the service's.
/// </summary>
public sealed class QueryableQueryTests
{
    private static IQueryable<Row> Rows =>
        new[]
        {
            new Row { Name = "a", Count = 1, Active = true, Secret = "s1" },
            new Row { Name = "b", Count = 2, Active = false, Secret = "s2" },
        }.AsQueryable();

    // Read from a request and never checked against a resource, an expression could name any
    // property of the entity, including one the resource never publishes.
    [Fact]
    public void Matching_ExpressionReadFromARequestButNeverChecked_IsRefused()
    {
        var query = Read("""{ "in": [{ "var": "secret" }, "s"] }""");

        var actual = Assert.Throws<InvalidOperationException>(() => Rows.Matching(query).ToList());

        Assert.Contains("never checked", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Matching_ExpressionParsedButNeverChecked_IsRefused()
    {
        using var document = JsonDocument.Parse("""{ "==": [{ "var": "secret" }, "s1"] }""");
        var query = Query.Parse(document.RootElement.Clone());

        Assert.Throws<InvalidOperationException>(() => Rows.Matching(query).ToList());
    }

    // Built from terms in code rather than read from text, it names the entity's own properties.
    [Fact]
    public void Matching_ExpressionBuiltInCode_NamesTheEntitysProperties()
    {
        using var literal = JsonDocument.Parse("\"b\"");
        var query = new Query
        {
            Root = new QueryTerm
            {
                Operator = QueryOperator.Equal,
                Terms =
                [
                    new QueryTerm { Operator = QueryOperator.Var, Field = "Name" },
                    new QueryTerm { Value = literal.RootElement.Clone() },
                ],
            },
        };

        Assert.Equal(["b"], Rows.Matching(query).Select(row => row.Name));
    }

    [Fact]
    public void Matching_CheckedExpression_Matches()
    {
        Assert.Equal(["b"], Rows.Matching(Checked("""{ "==": [{ "var": "count" }, 2] }""")).Select(row => row.Name));
    }

    [Theory]
    [InlineData("5", "/filter")]
    [InlineData("""{ "var": "name" }""", "/filter")]
    [InlineData("""{ "and": [{ "==": [{ "var": "name" }, "a"] }, 5] }""", "/filter/and/1")]
    [InlineData("""{ "or": [{ "var": "active" }, { "==": [{ "var": "name" }, "a"] }] }""", "/filter/or/0")]
    [InlineData("""{ "!": "a" }""", "/filter/!/0")]
    public void Parse_NotAComparisonWhereOneBelongs_IsRefusedAtIt(string filter, string pointer)
    {
        var actual = Assert.Throws<InvalidRequestException>(() => Read(filter));

        Assert.Equal(pointer, actual.Pointer);
    }

    [Fact]
    public void Parse_VarNamingNothing_IsRefused()
    {
        var actual = Assert.Throws<InvalidRequestException>(() => Read("""{ "==": [{ "var": "" }, "a"] }"""));

        Assert.Equal("/filter/==/0", actual.Pointer);
    }

    [Theory]
    [InlineData("""{ ">": [{ "var": "name" }, "a"] }""")]
    [InlineData("""{ "<=": [{ "var": "active" }, true] }""")]
    public void Matching_OrderingAFieldThatHasNoOrder_IsRefused(string filter)
    {
        var actual = Assert.Throws<InvalidRequestException>(() => Rows.Matching(Checked(filter)).ToList());

        Assert.Equal("/filter", actual.Pointer);
    }

    [Fact]
    public void Matching_OrderingANumber_Matches()
    {
        Assert.Equal(["b"], Rows.Matching(Checked("""{ ">": [{ "var": "count" }, 1] }""")).Select(row => row.Name));
    }

    [Theory]
    [InlineData("""{ "in": [{ "var": "count" }, [null]] }""")]
    [InlineData("""{ "in": [{ "var": "count" }, [1, null]] }""")]
    [InlineData("""{ "==": [{ "var": "count" }, null] }""")]
    public void Matching_NullAgainstAFieldThatCannotBeNull_IsRefused(string filter)
    {
        Assert.Throws<InvalidRequestException>(() => Rows.Matching(Checked(filter)).ToList());
    }

    [Fact]
    public void Matching_NullAgainstAFieldThatCanBe_Matches()
    {
        var rows = new[] { new Row { Name = "a", Score = null }, new Row { Name = "b", Score = 3 } }.AsQueryable();

        Assert.Equal(["a"], rows.Matching(Checked("""{ "in": [{ "var": "score" }, [null]] }""")).Select(row => row.Name));
    }

    [Fact]
    public void Matching_SubstringThatIsNotText_IsRefused()
    {
        Assert.Throws<InvalidRequestException>(() => Rows.Matching(Checked("""{ "in": [{ "var": "name" }, 5] }""")).ToList());
    }

    private static Query Read(string filter) => JsonSerializer.Deserialize<Query>(filter)!;

    private static Query Checked(string filter)
    {
        var query = Read(filter);
        ResourceFields.Require(query, typeof(Item));
        return query;
    }

    private sealed class Row
    {
        public string Name { get; set; } = "";

        public int Count { get; set; }

        public int? Score { get; set; }

        public bool Active { get; set; }

        public string Secret { get; set; } = "";
    }

    private sealed record Item : Resource
    {
        [Filterable]
        public string? Name { get; init; }

        [Filterable]
        public int Count { get; init; }

        [Filterable]
        public int? Score { get; init; }

        [Filterable]
        public bool Active { get; init; }
    }
}
