using System.Linq.Expressions;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// However wide a caller makes an expression, what it compiles to stays shallow.
/// </summary>
/// <remarks>
/// A query provider walks an expression recursively. An <c>and</c> or <c>or</c> of N terms folded
/// left is N deep, so a filter of tens of thousands of comparisons overflowed the stack inside the
/// provider, which ends the process. The expression is refused past <see cref="Query.MaxTerms"/>
/// terms, with a <c>422</c> as every other invalid expression, and the terms it does hold are joined
/// as a balanced tree.
/// </remarks>
public sealed class QueryTests
{
    [Fact]
    public void Parse_MoreTermsThanTheLimit_IsRefusedAtTheFilter()
    {
        var comparisons = Query.MaxTerms / 3 + 1;

        var actual = Assert.Throws<InvalidRequestException>(() => Parse(Or(comparisons)));

        Assert.Equal("/filter", actual.Pointer);
        Assert.Contains($"at most {Query.MaxTerms} terms", actual.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TensOfThousandsOfTerms_IsRefused()
    {
        Assert.Throws<InvalidRequestException>(() => Parse(Or(50_000)));
    }

    // The limit counts every operator, field and literal; an expression just inside it is read.
    [Fact]
    public void Parse_AsManyTermsAsTheLimit_IsRead()
    {
        var comparisons = (Query.MaxTerms - 1) / 3;

        var query = Parse(Or(comparisons));

        Assert.Equal(comparisons, query.Root.Terms.Count);
    }

    [Fact]
    public void Matching_ManyTerms_IsBalancedAndMatchesWhatAnyOfThemMatch()
    {
        var comparisons = (Query.MaxTerms - 1) / 3;
        var rows = Enumerable.Range(0, comparisons * 2).Select(index => new Row { Name = $"n{index}" }).ToArray().AsQueryable();

        var matched = rows.Matching(Checked(Or(comparisons)));

        Assert.Equal(Enumerable.Range(0, comparisons).Select(index => $"n{index}"), matched.Select(row => row.Name));
        Assert.True(
            Depth.Of(matched.Expression) <= 2 * (int)Math.Ceiling(Math.Log2(comparisons)) + 8,
            $"The expression is {Depth.Of(matched.Expression)} deep for {comparisons} terms.");
    }

    [Fact]
    public void Matching_And_IsBalancedToo()
    {
        var comparisons = (Query.MaxTerms - 1) / 3;
        var terms = string.Join(',', Enumerable.Range(0, comparisons).Select(index => $$"""{ "!=": [{ "var": "Name" }, "x{{index}}"] }"""));
        var rows = new[] { new Row { Name = "kept" }, new Row { Name = "x3" } }.AsQueryable();

        var matched = rows.Matching(Checked($$"""{ "and": [{{terms}}] }"""));

        Assert.Equal(["kept"], matched.Select(row => row.Name));
        Assert.True(Depth.Of(matched.Expression) <= 2 * (int)Math.Ceiling(Math.Log2(comparisons)) + 8);
    }

    private static string Or(int comparisons) =>
        $$"""{ "or": [{{string.Join(',', Enumerable.Range(0, comparisons).Select(index => $$"""{ "==": [{ "var": "Name" }, "n{{index}}"] }"""))}}] }""";

    private static Query Parse(string filter)
    {
        using var document = JsonDocument.Parse(filter, new JsonDocumentOptions { MaxDepth = 64 });
        return Query.Parse(document.RootElement.Clone());
    }

    private static Query Checked(string filter)
    {
        var query = Parse(filter);
        ResourceFields.Require(query, typeof(Item));
        return query;
    }

    private sealed class Row
    {
        public string Name { get; set; } = "";
    }

    private sealed record Item : Resource
    {
        [Filterable]
        public string? Name { get; init; }
    }

    /// <summary>
    /// How deep an expression tree is.
    /// </summary>
    private sealed class Depth : ExpressionVisitor
    {
        private int _current;
        private int _deepest;

        public static int Of(Expression expression)
        {
            var depth = new Depth();
            depth.Visit(expression);
            return depth._deepest;
        }

        public override Expression? Visit(Expression? node)
        {
            _current++;
            _deepest = Math.Max(_deepest, _current);
            try
            {
                return base.Visit(node);
            }
            finally
            {
                _current--;
            }
        }
    }
}
