using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// A page paged by cursor reads back from its wire shape, resources flattened and checked.
/// </summary>
public sealed class CursorPagedResourceListJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_WireShape_ResourcesAndCursor()
    {
        var actual = JsonSerializer.Deserialize<CursorPagedResourceListDocument<Widget>>(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}],"meta":{"hasMore":true,"cursor":"c-1"}}""",
            Options)!;

        Assert.Equal(new Widget { Id = "1", Name = "a" }, Assert.Single(actual.Data));
        Assert.Equal("c-1", ((JsonElement)actual.Meta!["cursor"]!).GetString());
    }

    [Fact]
    public void Read_AResourceOfAnotherType_Refused()
    {
        var actual = Assert.Throws<ResourceContractException>(() => JsonSerializer.Deserialize<CursorPagedResourceListDocument<Widget>>(
            """{"data":[{"type":"gadget","attributes":{}}]}""", Options));

        Assert.Equal("/data/0/type", actual.Pointer);
    }

    [Fact]
    public void Converter_IsTheCollectionConverter()
    {
        Assert.IsAssignableFrom<CollectionJsonConverter<CursorPagedResourceListDocument<Widget>, Widget>>(
            new CursorPagedResourceListJsonConverter<Widget>());
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
