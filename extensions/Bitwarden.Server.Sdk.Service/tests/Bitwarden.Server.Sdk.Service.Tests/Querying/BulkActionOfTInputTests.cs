using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk action with input carries it in <c>data</c>: one object every targeted resource gets, or
/// an array with one entry per resource, beside the usual targeting.
/// </summary>
public sealed class BulkActionOfTInputTests
{
    [Fact]
    public void Read_DataAsAnObject_IsTheInputForEveryResource()
    {
        var actual = JsonSerializer.Deserialize<BulkAction<Widget, Move>>(
            """{ "ids": ["a", "b"], "data": { "folder": "f" } }""", JsonSerializerOptions.Web)!;

        Assert.Equal(["a", "b"], actual.Ids);
        Assert.False(actual.Data!.IsPerResource);
        Assert.Equal("f", actual.Data.At(1)!.Folder);
    }

    [Fact]
    public void Read_DataAsAnArray_IsOneInputPerResource()
    {
        var actual = JsonSerializer.Deserialize<BulkAction<Widget, Move>>(
            """{ "ids": ["a", "b"], "data": [{ "folder": "f" }, { "folder": "g" }] }""", JsonSerializerOptions.Web)!;

        Assert.True(actual.Data!.IsPerResource);
        Assert.Equal(["f", "g"], actual.Data.Each!.Select(move => move.Folder));
    }

    [Fact]
    public void Read_NoData_IsNull()
    {
        var actual = JsonSerializer.Deserialize<BulkAction<Widget, Move>>("""{ "ids": ["a"] }""", JsonSerializerOptions.Web)!;

        Assert.Null(actual.Data);
    }

    public sealed record Widget : Resource;

    public sealed record Move(string Folder);
}
