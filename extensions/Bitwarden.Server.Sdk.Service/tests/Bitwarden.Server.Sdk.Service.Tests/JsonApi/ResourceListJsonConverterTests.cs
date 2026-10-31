using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// An unpaged collection reads back from its wire shape, every resource flattened and checked.
/// </summary>
public sealed class ResourceListJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_WireShape_FlatResourcesAndMeta()
    {
        var actual = JsonSerializer.Deserialize<ResourceListDocument<Widget>>(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}],"meta":{"note":"n"}}""", Options)!;

        Assert.Equal(new Widget { Id = "1", Name = "a" }, Assert.Single(actual.Data));
        Assert.Equal("n", ((JsonElement)actual.Meta!["note"]!).GetString());
    }

    [Fact]
    public void Read_AResourceOfAnotherType_Refused()
    {
        var actual = Assert.Throws<ResourceContractException>(() => JsonSerializer.Deserialize<ResourceListDocument<Widget>>(
            """{"data":[{"type":"gadget","attributes":{}}]}""", Options));

        Assert.Equal("/data/0/type", actual.Pointer);
    }

    [Fact]
    public void Converter_IsTheCollectionConverter()
    {
        Assert.IsAssignableFrom<CollectionJsonConverter<ResourceListDocument<Widget>, Widget>>(new ResourceListJsonConverter<Widget>());
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
