using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A page reads back from its wire shape, resources flattened and checked, links and meta kept.
/// </summary>
public sealed class PagedResourceListJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_WireShape_ResourcesLinksAndMeta()
    {
        var actual = JsonSerializer.Deserialize<PagedResourceListDocument<Widget>>(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}],"meta":{"totalCount":1},"links":{"first":"f","last":"l"}}""",
            Options)!;

        Assert.Equal(new Widget { Id = "1", Name = "a" }, Assert.Single(actual.Data));
        Assert.Equal(new PageLinks { First = "f", Last = "l" }, actual.Links);
        Assert.Equal(1, ((JsonElement)actual.Meta!["totalCount"]!).GetInt32());
        Assert.Null(actual.PendingLinks);
    }

    [Fact]
    public void Read_AResourceOfAnotherType_Refused()
    {
        var actual = Assert.Throws<ResourceContractException>(() => JsonSerializer.Deserialize<PagedResourceListDocument<Widget>>(
            """{"data":[{"type":"widget","attributes":{}},{"attributes":{}}]}""", Options));

        Assert.Equal("/data/1/type", actual.Pointer);
    }

    [Fact]
    public void Converter_IsTheCollectionConverter()
    {
        Assert.IsAssignableFrom<CollectionJsonConverter<PagedResourceListDocument<Widget>, Widget>>(new PagedResourceListJsonConverter<Widget>());
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
