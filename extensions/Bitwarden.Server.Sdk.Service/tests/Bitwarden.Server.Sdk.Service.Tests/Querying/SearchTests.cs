using System.Text.Json;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A search's body is its filter, required, and read as an expression from the request.
/// </summary>
public sealed class SearchTests
{
    [Fact]
    public void Read_Filter_IsAnExpressionReadFromText()
    {
        var actual = JsonSerializer.Deserialize<Search<Widget>>(
            """{ "filter": { "in": [{ "var": "name" }, "a"] } }""", JsonSerializerOptions.Web)!;

        Assert.Equal(QueryOperator.In, actual.Filter.Root.Operator);
        Assert.True(actual.Filter.ReadFromText);
    }

    [Fact]
    public void Read_NoFilter_IsRefused()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Search<Widget>>("{}", JsonSerializerOptions.Web));
    }

    [Fact]
    public void Read_FilterThatIsNotACondition_IsRefusedAtTheFilter()
    {
        var actual = Assert.Throws<InvalidRequestException>(() =>
            JsonSerializer.Deserialize<Search<Widget>>("""{ "filter": "a" }""", JsonSerializerOptions.Web));

        Assert.Equal("/filter", actual.Pointer);
    }

    public sealed record Widget : Resource;
}
