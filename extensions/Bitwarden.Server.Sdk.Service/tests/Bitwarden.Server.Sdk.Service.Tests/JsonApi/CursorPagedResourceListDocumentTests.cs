using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The wire shape of a page paged by cursor: resources and <c>meta</c>, with no links and no count.
/// </summary>
public sealed class CursorPagedResourceListDocumentTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Serialize_MoreToFollow_MetaCarriesTheCursor()
    {
        var document = new CursorPagedResourceListDocument<Widget>
        {
            Data = [new Widget { Id = "1", Name = "a" }],
            Meta = new Dictionary<string, object?> { ["pageSize"] = 1, ["hasMore"] = true, ["cursor"] = "c-1" },
        };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}}],"meta":{"pageSize":1,"hasMore":true,"cursor":"c-1"}}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Serialize_NoMeta_DataOnly()
    {
        Assert.Equal("""{"data":[]}""", JsonSerializer.Serialize(new CursorPagedResourceListDocument<Widget> { Data = [] }, Options));
    }

    // Not a sparse document and not a paged one: nothing writes fields or links on it.
    [Fact]
    public void Type_NeitherSparseNorLinked()
    {
        Assert.False(typeof(ISparseDocument).IsAssignableFrom(typeof(CursorPagedResourceListDocument<Widget>)));
        Assert.False(typeof(Bitwarden.Server.Sdk.Service.Endpoints.IDeferredLinks).IsAssignableFrom(typeof(CursorPagedResourceListDocument<Widget>)));
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
