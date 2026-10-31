using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The wire shape of a bulk create's answer: each created resource enveloped, its item's
/// <c>lid</c> echoed between <c>type</c> and <c>attributes</c>, and <c>meta.affectedCount</c>.
/// </summary>
public sealed class BulkCreatedJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Write_LidsEchoedBesideIdAndType()
    {
        var document = new BulkCreatedDocument<Widget>
        {
            Data = [new Widget { Id = "1", Name = "a" }, new Widget { Id = "2", Name = "b" }, new Widget { Id = "3" }],
            Meta = new Dictionary<string, object?> { ["affectedCount"] = 3 },
            Lids = ["one", null, "three"],
        };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","lid":"one","attributes":{"name":"a"}},{"id":"2","type":"widget","attributes":{"name":"b"}},{"id":"3","type":"widget","lid":"three","attributes":{}}],"meta":{"affectedCount":3}}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Write_NoLids_PlainResources()
    {
        var document = new BulkCreatedDocument<Widget> { Data = [new Widget { Id = "1" }] };

        Assert.Equal("""{"data":[{"id":"1","type":"widget","attributes":{}}]}""", JsonSerializer.Serialize(document, Options));
    }

    // Fewer lids than resources leaves the rest without one rather than failing the answer.
    [Fact]
    public void Write_FewerLidsThanResources_OnlyThoseEchoed()
    {
        var document = new BulkCreatedDocument<Widget> { Data = [new Widget { Id = "1" }, new Widget { Id = "2" }], Lids = ["one"] };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","lid":"one","attributes":{}},{"id":"2","type":"widget","attributes":{}}]}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Write_ANullResource_NoLidForIt()
    {
        var document = new BulkCreatedDocument<Widget> { Data = [null!, new Widget { Id = "2" }], Lids = ["one", "two"] };

        Assert.Equal(
            """{"data":[null,{"id":"2","type":"widget","lid":"two","attributes":{}}]}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Read_WireShape_ResourcesAndCount()
    {
        var actual = JsonSerializer.Deserialize<BulkCreatedDocument<Widget>>(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}],"meta":{"affectedCount":1}}""", Options)!;

        Assert.Equal(new Widget { Id = "1", Name = "a" }, Assert.Single(actual.Data));
        Assert.Equal(1, ((JsonElement)actual.Meta!["affectedCount"]!).GetInt32());
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
