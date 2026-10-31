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
            new Row { Name = "b", Count = 2, Active = false, Secret = "s2", Shade = Shade.DarkBlue },
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

    [Theory]
    [InlineData("""{ "!=": [{ "var": "count" }, 1] }""", new[] { "b" })]
    [InlineData("""{ ">=": [{ "var": "count" }, 2] }""", new[] { "b" })]
    [InlineData("""{ "<": [{ "var": "count" }, 2] }""", new[] { "a" })]
    [InlineData("""{ "<=": [{ "var": "count" }, 2] }""", new[] { "a", "b" })]
    [InlineData("""{ "!": { "==": [{ "var": "name" }, "a"] } }""", new[] { "b" })]
    [InlineData("""{ "or": [{ "==": [{ "var": "name" }, "a"] }, { "==": [{ "var": "count" }, 2] }] }""", new[] { "a", "b" })]
    [InlineData("""{ "and": [{ "==": [{ "var": "active" }, true] }, { "==": [{ "var": "count" }, 2] }] }""", new string[0])]
    [InlineData("""{ "in": [{ "var": "count" }, [2, 3]] }""", new[] { "b" })]
    [InlineData("""{ "in": [{ "var": "name" }, "b"] }""", new[] { "b" })]
    [InlineData("""{ "==": [5, { "var": "count" }] }""", new string[0])]
    public void Matching_EachOperator_MatchesWhatItSays(string filter, string[] expected)
    {
        Assert.Equal(expected, Rows.Matching(Checked(filter)).Select(row => row.Name));
    }

    // The literal may come first or second, and each ordering reads as written: '2 < count' is
    // the rows whose count is above 2, not below it.
    [Theory]
    [InlineData("""{ "<": [{ "var": "count" }, 2] }""", new[] { "a" })]
    [InlineData("""{ "<": [2, { "var": "count" }] }""", new[] { "c" })]
    [InlineData("""{ "<=": [{ "var": "count" }, 2] }""", new[] { "a", "b" })]
    [InlineData("""{ "<=": [2, { "var": "count" }] }""", new[] { "b", "c" })]
    [InlineData("""{ ">": [{ "var": "count" }, 2] }""", new[] { "c" })]
    [InlineData("""{ ">": [2, { "var": "count" }] }""", new[] { "a" })]
    [InlineData("""{ ">=": [{ "var": "count" }, 2] }""", new[] { "b", "c" })]
    [InlineData("""{ ">=": [2, { "var": "count" }] }""", new[] { "a", "b" })]
    [InlineData("""{ "==": [2, { "var": "count" }] }""", new[] { "b" })]
    [InlineData("""{ "!=": [2, { "var": "count" }] }""", new[] { "a", "c" })]
    public void Matching_OrderingWithTheLiteralFirstOrSecond_ReadsAsWritten(string filter, string[] expected)
    {
        var rows = new[]
        {
            new Row { Name = "a", Count = 1 },
            new Row { Name = "b", Count = 2 },
            new Row { Name = "c", Count = 3 },
        }.AsQueryable();

        Assert.Equal(expected, rows.Matching(Checked(filter)).Select(row => row.Name));
    }

    [Fact]
    public void Matching_Enumeration_IsComparedByItsPublishedName()
    {
        Assert.Equal(["b"], Rows.Matching(Checked("""{ "==": [{ "var": "shade" }, "DARK_BLUE"] }""")).Select(row => row.Name));
    }

    [Theory]
    [InlineData("""{ "==": [{ "var": "shade" }, "DarkBlue"] }""")]
    [InlineData("""{ "==": [{ "var": "count" }, "x"] }""")]
    [InlineData("""{ "in": [{ "var": "count" }, ["x"]] }""")]
    [InlineData("""{ "==": [{ "var": "shade" }, 1] }""")]
    [InlineData("""{ "==": [{ "var": "shade" }, true] }""")]
    [InlineData("""{ "in": [{ "var": "shade" }, [1]] }""")]
    public void Matching_LiteralTheFieldCannotHold_IsRefused(string filter)
    {
        var actual = Assert.Throws<InvalidRequestException>(() => Rows.Matching(Checked(filter)).ToList());

        Assert.Equal("/filter", actual.Pointer);
        Assert.Contains("is not a value this field can be compared against", actual.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "and": { "==": [{ "var": "name" }, "a"] } }""", "combines two or more terms")]
    [InlineData("""{ "or": [] }""", "combines two or more terms")]
    [InlineData("""{ "!": [{ "==": [{ "var": "name" }, "a"] }, { "==": [{ "var": "name" }, "b"] }] }""", "negates exactly one term")]
    [InlineData("""{ "==": [{ "var": "name" }] }""", "compares exactly two operands")]
    [InlineData("""{ "==": [{ "var": "name" }, "a", "b"] }""", "compares exactly two operands")]
    [InlineData("""{ "==": [{ "var": "name" }, { "var": "name" }] }""", "between one field and one value")]
    [InlineData("""{ "==": [1, 2] }""", "between one field and one value")]
    public void Matching_OperatorGivenTheWrongNumberOrKindOfOperands_IsRefused(string filter, string expected)
    {
        var actual = Assert.Throws<InvalidRequestException>(() => Rows.Matching(Checked(filter)).ToList());

        Assert.Equal("/filter", actual.Pointer);
        Assert.Contains(expected, actual.Message, StringComparison.Ordinal);
    }

    // A query built in code is the service's own; a mistake in it is a wiring error, not a 422.
    [Fact]
    public void Matching_ExpressionBuiltInCodeNamingNoSuchProperty_IsAWiringError()
    {
        using var literal = JsonDocument.Parse("\"b\"");
        var query = new Query
        {
            Root = new QueryTerm
            {
                Operator = QueryOperator.Equal,
                Terms = [new QueryTerm { Operator = QueryOperator.Var, Field = "nope" }, new QueryTerm { Value = literal.RootElement.Clone() }],
            },
        };

        var actual = Assert.Throws<InvalidOperationException>(() => Rows.Matching(query).ToList());

        Assert.Contains("has no property 'nope'", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Matching_ExpressionCheckedForAResourceNamingAFieldItDoesNotFilterOn_IsAWiringError()
    {
        using var literal = JsonDocument.Parse("\"s\"");
        var query = new Query
        {
            Root = new QueryTerm
            {
                Operator = QueryOperator.Equal,
                Terms = [new QueryTerm { Operator = QueryOperator.Var, Field = "secret" }, new QueryTerm { Value = literal.RootElement.Clone() }],
            },
            Resource = typeof(Item),
        };

        var actual = Assert.Throws<InvalidOperationException>(() => Rows.Matching(query).ToList());

        Assert.Contains("'secret' is not a filterable field", actual.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(QueryOperator.Var)]
    public void Matching_RootThatIsNotACondition_IsAWiringError(QueryOperator? root)
    {
        var query = new Query { Root = new QueryTerm { Operator = root, Field = "name" } };

        Assert.Throws<InvalidOperationException>(() => Rows.Matching(query).ToList());
    }

    [Fact]
    public void Matching_NoSourceOrQuery_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((IQueryable<Row>)null!).Matching(Checked("""{ "==": [{ "var": "count" }, 2] }""")));
        Assert.Throws<ArgumentNullException>(() => Rows.Matching(null!));
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

        public Shade Shade { get; set; }
    }

    public enum Shade
    {
        Light,
        DarkBlue,
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

        [Filterable]
        public Shade Shade { get; init; }
    }
}
