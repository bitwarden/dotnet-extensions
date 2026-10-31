using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk create's <c>lid</c>: read off each item, kept in order, and echoed beside the created
/// resource's <c>id</c>, as the standards and JSON:API place it.
/// </summary>
public sealed class BulkCreateTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_ItemsCarryingLids_KeepsThemInItemOrder_AndOffTheResource()
    {
        var actual = JsonSerializer.Deserialize<BulkCreate<Widget>>(
            """
            { "data": [
              { "type": "widget", "lid": "one", "attributes": { "name": "a" } },
              { "type": "widget", "attributes": { "name": "b" } },
              { "type": "widget", "lid": "three", "attributes": { "name": "c" } }
            ] }
            """,
            Options)!;

        Assert.Equal(["one", null, "three"], actual.Lids);
        Assert.Equal(["a", "b", "c"], actual.Data.Select(widget => widget.Name));
        Assert.Null(actual.LidAt(7));
    }

    [Fact]
    public void Read_TwoItemsSharingALid_RefusedPointingAtTheSecond()
    {
        var actual = Assert.Throws<ResourceContractException>(() => JsonSerializer.Deserialize<BulkCreate<Widget>>(
            """{ "data": [ { "type": "widget", "lid": "x", "attributes": { "name": "a" } }, { "type": "widget", "lid": "x", "attributes": { "name": "b" } } ] }""",
            Options));

        Assert.Equal("/data/1/lid", actual.Pointer);
        Assert.Equal("$.data[1].lid", actual.Path);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void Read_ALidThatIsNotANonEmptyString_Refused(string lid)
    {
        var actual = Assert.Throws<ResourceContractException>(() => JsonSerializer.Deserialize<BulkCreate<Widget>>(
            $$"""{ "data": [ { "type": "widget", "lid": {{lid}}, "attributes": { "name": "a" } } ] }""",
            Options));

        Assert.Equal("/data/0/lid", actual.Pointer);
    }

    [Fact]
    public void Write_EchoesEachLidBesideItsId_AndCountsWhatWasMade()
    {
        var request = new BulkCreate<Widget> { Data = [new Widget { Name = "a" }, new Widget { Name = "b" }] };
        request.Lids = ["one", null];

        var document = BulkCreatedDocument<Widget>.For(
            request, [new Widget { Id = "1", Name = "a" }, new Widget { Id = "2", Name = "b" }]);

        var actual = JsonNode.Parse(JsonSerializer.Serialize(document, Options))!;

        Assert.Equal("one", actual["data"]![0]!["lid"]!.GetValue<string>());
        Assert.Equal("1", actual["data"]![0]!["id"]!.GetValue<string>());
        Assert.Null(actual["data"]![0]!["attributes"]!["lid"]);
        Assert.Null(actual["data"]![1]!["lid"]);
        Assert.Equal(2, actual["meta"]!["affectedCount"]!.GetValue<int>());
        Assert.Equal(
            ["id", "type", "lid", "attributes"],
            actual["data"]![0]!.AsObject().Select(member => member.Key));
    }

    // All-or-nothing answers one for one; anything else is a bug in the operation, caught here.
    [Fact]
    public void For_FewerMadeThanAskedFor_Throws()
    {
        var request = new BulkCreate<Widget> { Data = [new Widget { Name = "a" }, new Widget { Name = "b" }] };

        Assert.Throws<ArgumentException>(() => BulkCreatedDocument<Widget>.For(request, [new Widget { Id = "1", Name = "a" }]));
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
