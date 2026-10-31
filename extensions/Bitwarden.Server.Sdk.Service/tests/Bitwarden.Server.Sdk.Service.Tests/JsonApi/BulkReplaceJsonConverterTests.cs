using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A bulk replace's items, each shaped as a single replace is: enveloped on the wire, flattened
/// when read, and each one's <c>type</c> enforced.
/// </summary>
public sealed class BulkReplaceJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_Items_FlattenedWithTheirIdsAndVersions()
    {
        var actual = JsonSerializer.Deserialize<BulkReplace<Widget>>(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a","version":"4"}},{"id":"2","type":"widget","attributes":{"name":"b"}}]}""",
            Options)!;

        Assert.Equal([new Widget { Id = "1", Name = "a", Version = "4" }, new Widget { Id = "2", Name = "b" }], actual.Data);
        Assert.Equal(Atomicity.AllOrNothing, actual.Atomicity);
    }

    [Fact]
    public void Read_AnItemOfAnotherType_RefusedAtIt()
    {
        var actual = Assert.Throws<ResourceContractException>(() => JsonSerializer.Deserialize<BulkReplace<Widget>>(
            """{"data":[{"id":"1","type":"gadget","attributes":{}}]}""", Options));

        Assert.Equal("/data/0/type", actual.Pointer);
    }

    [Fact]
    public void Write_Items_Enveloped()
    {
        var request = new BulkReplace<Widget> { Data = [new Widget { Id = "1", Name = "a" }] };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}]}""",
            JsonSerializer.Serialize(request, Options));
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
