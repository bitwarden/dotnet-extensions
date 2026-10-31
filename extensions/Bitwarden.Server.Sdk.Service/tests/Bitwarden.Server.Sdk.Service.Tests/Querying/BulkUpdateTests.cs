using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk update carries the changes as sent, member by member, so the operation can tell a
/// member set to null from one left out.
/// </summary>
public sealed class BulkUpdateTests
{
    [Fact]
    public void Read_Update_KeepsEachMemberAsSent()
    {
        var actual = JsonSerializer.Deserialize<BulkUpdate<Widget>>(
            """{ "ids": ["a"], "update": { "name": "b", "notes": null } }""", JsonSerializerOptions.Web)!;

        Assert.Equal(["name", "notes"], actual.Update.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("b", actual.Update["name"].GetString());
        Assert.Equal(JsonValueKind.Null, actual.Update["notes"].ValueKind);
        Assert.False(actual.Update.ContainsKey("folder"));
    }

    [Fact]
    public void Read_NoUpdate_IsRefused()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<BulkUpdate<Widget>>("""{ "ids": ["a"] }""", JsonSerializerOptions.Web));
    }

    public sealed record Widget : Resource;
}
