namespace Bitwarden.Server.Sdk.RestrictedDependencies.Tests;

/// <summary>
/// The reader is hand-rolled because an analyzer cannot carry System.Text.Json, so its own failure
/// paths are covered here rather than only through the baseline documents that use it.
/// </summary>
public class BudgetJsonSerializerTests
{
    /// <summary>
    /// A baseline's root is always an object. Whitespace anywhere around a token must parse, so a
    /// CRLF checkout reads the same as an LF one.
    /// </summary>
    [Theory]
    [InlineData("{}", 0)]
    [InlineData("  \t\r\n { \"a\" : 1 }  \r\n ", 1)]
    public void Parse_AcceptsAnObjectRootWithWhitespaceAnywhere(string json, int members) =>
        Assert.Equal(members, Assert.IsType<Dictionary<string, object?>>(BudgetJsonSerializer.Parse(json)).Count);

    [Theory]
    [InlineData("")] // empty input
    [InlineData("{")] // unclosed object
    [InlineData("[")] // unclosed array
    [InlineData("{ \"a\": 1 ")] // unclosed object after a member
    [InlineData("[ 1, ")] // unclosed array after an element
    [InlineData("{ \"a\" 1 }")] // missing colon
    [InlineData("{ a: 1 }")] // unquoted key
    [InlineData("\"unterminated")] // unterminated string
    [InlineData("\"bad \\q escape\"")] // unknown escape
    [InlineData("\"trailing backslash \\")] // unterminated escape
    [InlineData("\"\\u12\"")] // truncated unicode escape
    [InlineData("{} trailing")] // trailing content
    [InlineData("nul")] // truncated literal
    [InlineData("-")] // sign with no digits
    [InlineData("1.5")] // a decimal, which the baseline schema has no use for
    [InlineData("99999999999999999999")] // an integer too large for long
    [InlineData("@")] // a character that starts no token
    public void Parse_RejectsMalformedInput(string json) =>
        Assert.Throws<FormatException>(() => BudgetJsonSerializer.Parse(json));

    [Fact]
    public void Parse_RejectsDeeplyNestedInput_RatherThanOverflowingTheStack()
    {
        // A StackOverflowException cannot be caught, so unbounded nesting would take down the
        // compiler hosting the analyzer instead of being reported as a malformed baseline.
        var deep = new string('[', 5_000) + new string(']', 5_000);

        var error = Assert.Throws<FormatException>(() => BudgetJsonSerializer.Parse(deep));

        Assert.Contains("levels deep", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AcceptsTheNestingTheBaselineFormatActuallyUses()
    {
        // document -> sites array -> site object is three levels; the limit must clear it easily.
        BudgetJsonSerializer.Parse("{ \"sites\": [ { \"kind\": \"member\", \"nested\": [ [ { \"a\": 1 } ] ] } ] }");
    }

    [Fact]
    public void Parse_ReadsEveryValueTypeTheGrammarAllows()
    {
        var value = Assert.IsType<Dictionary<string, object?>>(
            BudgetJsonSerializer.Parse("{ \"n\": null, \"t\": true, \"f\": false, \"zero\": 0, \"negative\": -12, \"positive\": 42, \"empty\": \"\" }"));

        Assert.Null(value["n"]);
        Assert.True(Assert.IsType<bool>(value["t"]));
        Assert.False(Assert.IsType<bool>(value["f"]));
        Assert.Equal(0L, Assert.IsType<long>(value["zero"]));
        Assert.Equal(-12L, Assert.IsType<long>(value["negative"]));
        Assert.Equal(42L, Assert.IsType<long>(value["positive"]));
        Assert.Equal(string.Empty, value["empty"]);
    }

    [Fact]
    public void Parse_ReadsEscapesAndUnicode()
    {
        var value = Assert.IsType<Dictionary<string, object?>>(
            BudgetJsonSerializer.Parse("{ \"k\": \"a\\\"b\\\\c\\/d\\be\\ff\\ng\\rh\\ti\\u0041\" }"));

        Assert.Equal("a\"b\\c/d\be\ff\ng\rh\tiA", value["k"]);
    }

    [Fact]
    public void WriteString_EscapesControlCharactersAndQuotes()
    {
        var builder = new System.Text.StringBuilder();

        BudgetJsonSerializer.WriteString(builder, "a\"b\\c\nd\re\tf\u0001");

        Assert.Equal("\"a\\\"b\\\\c\\nd\\re\\tf\\u0001\"", builder.ToString());
    }

    [Fact]
    public void WriteString_RoundTripsThroughParse()
    {
        const string original = "M:Bit.Api.X.Run(System.String[],\"quoted\",\\slash\\)";
        var builder = new System.Text.StringBuilder();
        BudgetJsonSerializer.WriteString(builder, original);

        var parsed = Assert.IsType<Dictionary<string, object?>>(BudgetJsonSerializer.Parse($"{{ \"k\": {builder} }}"));

        Assert.Equal(original, parsed["k"]);
    }
}
