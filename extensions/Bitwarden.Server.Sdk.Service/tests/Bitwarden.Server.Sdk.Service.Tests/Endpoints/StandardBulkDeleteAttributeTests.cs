using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Endpoints;

/// <summary>
/// A bulk delete is a <c>POST</c> to <c>{collection}:bulk-delete</c> that succeeds with <c>200 OK</c>, all-or-nothing and targeted either way unless it says otherwise.
/// </summary>
public sealed class StandardBulkDeleteAttributeTests
{
    [Fact]
    public void Collection_Given_PostToItsSuffix()
    {
        var actual = new StandardBulkDeleteAttribute("api/v1/items");

        Assert.Equal(["POST"], actual.HttpMethods);
        Assert.Equal("~/api/v1/items:bulk-delete", actual.Template);
        Assert.Equal(200, actual.SuccessStatusCode);
        Assert.False(((IStandardOperation)actual).IsRead);
    }

    // A stray slash on either end cannot double the separator or make the suffix a segment.
    [Fact]
    public void Collection_WithSlashes_IsTrimmed()
    {
        Assert.Equal("~/api/v1/items:bulk-delete", new StandardBulkDeleteAttribute("/api/v1/items/").Template);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Collection_Blank_Refused(string collection)
    {
        Assert.ThrowsAny<ArgumentException>(() => new StandardBulkDeleteAttribute(collection));
    }

    // The safer of the two modes, so an operation that says nothing never answers best-effort.
    [Fact]
    public void Supports_Default_IsAllOrNothing()
    {
        Assert.Equal(Atomicity.AllOrNothing, new StandardBulkDeleteAttribute("api/v1/items").Supports);
    }

    [Fact]
    public void Supports_Set_IsKept()
    {
        var actual = new StandardBulkDeleteAttribute("api/v1/items") { Supports = Atomicity.AllOrNothing | Atomicity.BestEffort };

        Assert.Equal(Atomicity.AllOrNothing | Atomicity.BestEffort, actual.Supports);
    }

    [Fact]
    public void Targeting_Default_IsEither()
    {
        Assert.Equal(Targeting.Either, new StandardBulkDeleteAttribute("api/v1/items").Targeting);
    }

    [Fact]
    public void Targeting_Set_IsKept()
    {
        Assert.Equal(Targeting.Ids, new StandardBulkDeleteAttribute("api/v1/items") { Targeting = Targeting.Ids }.Targeting);
    }
}
