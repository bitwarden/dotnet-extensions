using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A query is read from a request as an expression, marked as read from text so it cannot run
/// until checked against its resource, and is never written back.
/// </summary>
public sealed class QueryJsonConverterTests
{
    [Fact]
    public void Read_Expression_IsParsedAndMarkedAsReadFromText()
    {
        var actual = JsonSerializer.Deserialize<Query>("""{ "==": [{ "var": "name" }, "a"] }""")!;

        Assert.Equal(QueryOperator.Equal, actual.Root.Operator);
        Assert.Equal("name", actual.Root.Terms[0].Field);
        Assert.True(actual.ReadFromText);
        Assert.Null(actual.Resource);
    }

    // Kept after the document it was read from is gone.
    [Fact]
    public void Read_Literal_OutlivesTheReader()
    {
        var actual = JsonSerializer.Deserialize<Query>("""{ "==": [{ "var": "name" }, "a"] }""")!;

        Assert.Equal("a", actual.Root.Terms[1].Value!.Value.GetString());
    }

    [Fact]
    public void Read_NotAnExpression_IsRefusedAtTheFilter()
    {
        var actual = Assert.Throws<InvalidRequestException>(() => JsonSerializer.Deserialize<Query>("5"));

        Assert.Equal("/filter", actual.Pointer);
    }

    [Fact]
    public void Write_IsNotSupported()
    {
        var query = JsonSerializer.Deserialize<Query>("""{ "==": [{ "var": "name" }, "a"] }""")!;

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(query));
    }
}
