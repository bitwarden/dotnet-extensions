using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Resources.Querying;

namespace Bitwarden.Server.Sdk.Resources.Tests;

/// <summary>
/// A bulk replace's items, each shaped as a single replace is: enveloped on the wire, flattened
/// when read, each one's <c>type</c> enforced, and each one cleaned as a single replace's resource is.
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
    public void Read_ReadOnlyAndUnknownAttributes_Dropped()
    {
        var actual = Read(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a","updatedBy":"x","retired":"r"}}]}""");

        Assert.Equal([new Widget { Id = "1", Name = "a" }], actual.Data);
    }

    [Theory]
    [InlineData("updatedBy")]
    [InlineData("UpdatedBy")]
    public void Read_AMemberBesideAttributes_Dropped(string member)
    {
        var actual = Read($$"""{"data":[{"id":"1","type":"widget","{{member}}":"x","attributes":{"name":"a"} }]}""");

        Assert.Equal([new Widget { Id = "1", Name = "a" }], actual.Data);
    }

    [Fact]
    public void Read_StringsTrimmedAndBlankOnesDropped()
    {
        var actual = Read("""{"data":[{"id":"1","type":"widget","attributes":{"name":" a ","version":""}}]}""");

        Assert.Equal([new Widget { Id = "1", Name = "a" }], actual.Data);
    }

    // The first item's type is right in all but case, and a type is matched exactly.
    [Fact]
    public void Read_AnItemsTypeInAnotherCase_RefusedAtIt()
    {
        var actual = Assert.Throws<ResourceContractException>(() => Read(
            """{"data":[{"id":"1","type":"Widget","attributes":{}}]}"""));

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

    private static BulkReplace<Widget> Read(string json) =>
        JsonSerializer.Deserialize<BulkReplace<Widget>>(json, Options)!;

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
