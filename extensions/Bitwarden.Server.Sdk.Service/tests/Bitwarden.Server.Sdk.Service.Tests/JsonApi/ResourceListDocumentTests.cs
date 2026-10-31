using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The wire shape of a collection returned whole: enveloped resources under <c>data</c>, <c>meta</c>
/// only when set, no <c>links</c>, and a sparse fieldset applied to every resource.
/// </summary>
public sealed class ResourceListDocumentTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Serialize_Resources_Enveloped()
    {
        var document = new ResourceListDocument<Widget>
        {
            Data = [new Widget { Id = "1", Name = "a", Size = 1 }, new Widget { Id = "2", Name = "b" }],
            Meta = new Dictionary<string, object?> { ["note"] = "n" },
        };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a","size":1}},{"id":"2","type":"widget","attributes":{"name":"b"}}],"meta":{"note":"n"}}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Serialize_Empty_AnEmptyArray_MetaAbsent()
    {
        Assert.Equal("""{"data":[]}""", JsonSerializer.Serialize(new ResourceListDocument<Widget> { Data = [] }, Options));
    }

    [Fact]
    public void Serialize_Fields_EachResourceSparse_FieldsNotWritten()
    {
        var document = new ResourceListDocument<Widget>
        {
            Data = [new Widget { Id = "1", Name = "a", Size = 1 }, new Widget { Id = "2", Size = 2 }],
            Fields = new HashSet<string> { "size" },
        };

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","attributes":{"size":1}},{"id":"2","type":"widget","attributes":{"size":2}}]}""",
            JsonSerializer.Serialize(document, Options));
    }

    [Fact]
    public void Fields_AreTheSparseDocumentsFields()
    {
        var fields = new HashSet<string> { "size" };

        ISparseDocument document = new ResourceListDocument<Widget> { Data = [], Fields = fields };

        Assert.Same(fields, document.Fields);
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }

        public int? Size { get; init; }
    }
}
