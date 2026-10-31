using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// Reading a bulk create: each item's <c>lid</c> taken off it and kept in item order, a
/// <c>lid</c> that is not a non-empty string or is shared refused at that item, and each item's
/// <c>type</c> enforced.
/// </summary>
public sealed class BulkCreateJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_NotAnObject_Refused()
    {
        var actual = Assert.Throws<JsonException>(() => Read("[]"));

        Assert.Equal("A resource collection document must be a JSON object.", actual.Message);
    }

    [Fact]
    public void Read_Items_FlattenedWithLidsInItemOrder()
    {
        var actual = Read(
            """{"data":[{"type":"widget","lid":"one","attributes":{"name":"a"}},{"type":"widget","attributes":{"name":"b"}}]}""");

        Assert.Equal([new Widget { Name = "a" }, new Widget { Name = "b" }], actual.Data);
        Assert.Equal(["one", null], actual.Lids);
    }

    [Fact]
    public void Read_AnItemThatIsNotAnObject_NoLidForIt()
    {
        var actual = Read("""{"data":[null,{"type":"widget","lid":"two","attributes":{}}]}""");

        Assert.Equal([null, "two"], actual.Lids);
        Assert.Null(actual.Data[0]);
    }

    [Fact]
    public void Read_LidsMatchCaseSensitively_SoTwoCasingsAreTwoLids()
    {
        var actual = Read(
            """{"data":[{"type":"widget","lid":"x","attributes":{}},{"type":"widget","lid":"X","attributes":{}}]}""");

        Assert.Equal(["x", "X"], actual.Lids);
    }

    [Fact]
    public void Read_TwoItemsSharingALid_RefusedAtTheSecond()
    {
        var actual = Assert.Throws<ResourceContractException>(() => Read(
            """{"data":[{"type":"widget","lid":"x","attributes":{}},{"type":"widget","attributes":{}},{"type":"widget","lid":"x","attributes":{}}]}"""));

        Assert.Equal("/data/2/lid", actual.Pointer);
        Assert.Equal("Two items carry the \"lid\" \"x\". Each item's local identifier must be its own.", actual.Message);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[\"x\"]")]
    public void Read_ALidThatIsNotANonEmptyString_Refused(string lid)
    {
        var actual = Assert.Throws<ResourceContractException>(() => Read(
            $$$"""{"data":[{"type":"widget","attributes":{}},{"type":"widget","lid":{{{lid}}},"attributes":{}}]}"""));

        Assert.Equal("/data/1/lid", actual.Pointer);
        Assert.Equal("A \"lid\" is a non-empty string the caller chooses.", actual.Message);
    }

    [Fact]
    public void Read_AnItemOfAnotherType_RefusedAtIt()
    {
        var actual = Assert.Throws<ResourceContractException>(() => Read(
            """{"data":[{"type":"widget","attributes":{}},{"type":"gadget","attributes":{}}]}"""));

        Assert.Equal("/data/1/type", actual.Pointer);
    }

    [Fact]
    public void Write_EnvelopedWithoutLids()
    {
        var request = new BulkCreate<Widget> { Data = [new Widget { Name = "a" }] };
        request.Lids = ["one"];

        Assert.Equal(
            """{"data":[{"type":"widget","attributes":{"name":"a"}}]}""",
            JsonSerializer.Serialize(request, Options));
    }

    private static BulkCreate<Widget> Read(string json) =>
        JsonSerializer.Deserialize<BulkCreate<Widget>>(json, Options)!;

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
