using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk delete is its targeting and nothing else: the ids or the filter of what to delete.
/// </summary>
public sealed class BulkDeleteTests
{
    [Fact]
    public void Read_IdsAndFilter_AreBothRead_ForTheChecksToRefuse()
    {
        var actual = JsonSerializer.Deserialize<BulkDelete<Widget>>(
            """{ "ids": ["a"], "filter": { "==": [{ "var": "name" }, "a"] } }""", JsonSerializerOptions.Web)!;

        Assert.Equal(["a"], actual.Ids);
        Assert.NotNull(actual.Filter);
    }

    [Fact]
    public void Read_Empty_TargetsNothing()
    {
        var actual = JsonSerializer.Deserialize<BulkDelete<Widget>>("{}", JsonSerializerOptions.Web)!;

        Assert.Null(actual.Ids);
        Assert.Null(actual.Filter);
    }

    [Fact]
    public void Write_HasNoAtomicityMember()
    {
        var actual = JsonNode.Parse(JsonSerializer.Serialize(new BulkDelete<Widget> { Ids = ["a"] }, JsonSerializerOptions.Web))!;

        Assert.Equal(["ids", "filter"], actual.AsObject().Select(member => member.Key));
    }

    public sealed record Widget : Resource;
}
