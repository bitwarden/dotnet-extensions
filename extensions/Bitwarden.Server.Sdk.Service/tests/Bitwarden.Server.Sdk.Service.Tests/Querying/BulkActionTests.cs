using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk action without input is read as its targeting alone: the ids or the filter it names.
/// </summary>
public sealed class BulkActionTests
{
    [Fact]
    public void Read_Ids_AreKeptInOrder()
    {
        var actual = JsonSerializer.Deserialize<BulkAction<Widget>>("""{ "ids": ["b", "a"] }""", JsonSerializerOptions.Web)!;

        Assert.Equal(["b", "a"], actual.Ids);
        Assert.Null(actual.Filter);
    }

    [Fact]
    public void Read_Filter_IsAnExpression()
    {
        var actual = JsonSerializer.Deserialize<BulkAction<Widget>>(
            """{ "filter": { "==": [{ "var": "name" }, "a"] } }""", JsonSerializerOptions.Web)!;

        Assert.Equal(QueryOperator.Equal, actual.Filter!.Root.Operator);
        Assert.Null(actual.Ids);
    }

    public sealed record Widget : Resource;
}
