using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The behaviour every collection document shares: each resource in <c>data</c> enveloped on the
/// way out and flattened on the way in, each one's <c>type</c> enforced at its own index, and a
/// document's extras added once the resources are enveloped.
/// </summary>
public sealed class CollectionJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Read_NotAnObject_Refused()
    {
        var actual = Assert.Throws<JsonException>(() => Read("[]", new Probe()));

        Assert.Equal("A resource collection document must be a JSON object.", actual.Message);
    }

    [Fact]
    public void Read_EachResource_Flattened()
    {
        var actual = Read("""{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}},{"id":"2","type":"widget","attributes":{"name":"b"}}]}""", new Probe());

        Assert.Equal([new Widget { Id = "1", Name = "a" }, new Widget { Id = "2", Name = "b" }], actual.Data);
    }

    [Fact]
    public void Read_OneResourceOfAnotherType_RefusedAtItsIndex()
    {
        var actual = Assert.Throws<ResourceContractException>(() => Read(
            """{"data":[{"type":"widget","attributes":{}},{"type":"widget","attributes":{}},{"type":"gadget","attributes":{}}]}""",
            new Probe()));

        Assert.Equal("/data/2/type", actual.Pointer);
        Assert.Equal("$.data[2].type", actual.Path);
    }

    [Fact]
    public void Read_AnElementThatIsNotAnObject_PassedOnAsItCame()
    {
        var actual = Read("""{"data":[null,{"type":"widget","attributes":{"name":"a"}}]}""", new Probe());

        Assert.Null(actual.Data[0]);
        Assert.Equal("a", actual.Data[1].Name);
    }

    [Fact]
    public void Read_DataNotAnArray_LeftForTheSerializerToRefuse()
    {
        Assert.ThrowsAny<JsonException>(() => Read("""{"data":{"type":"widget"}}""", new Probe()));
    }

    [Fact]
    public void Write_EachResourceEnveloped_DecoratedAfterwards()
    {
        var probe = new Probe();
        var document = new ResourceListDocument<Widget> { Data = [new Widget { Id = "1", Name = "a" }, new Widget()] };

        var actual = Write(document, probe);

        Assert.Equal(
            """{"data":[{"id":"1","type":"widget","attributes":{"name":"a"}},{"type":"widget","attributes":{}}]}""",
            actual);
        Assert.NotNull(probe.Decorated);
        Assert.All(probe.Decorated, resource => Assert.NotNull(resource!["attributes"]));
    }

    [Fact]
    public void Write_ANullResource_WrittenAsNull()
    {
        var document = new ResourceListDocument<Widget> { Data = [null!, new Widget { Id = "1" }] };

        Assert.Equal("""{"data":[null,{"id":"1","type":"widget","attributes":{}}]}""", Write(document, new Probe()));
    }

    private static ResourceListDocument<Widget> Read(string json, Probe probe)
    {
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read();

        return probe.Read(ref reader, typeof(ResourceListDocument<Widget>), Options);
    }

    private static string Write(ResourceListDocument<Widget> document, Probe probe)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
        {
            probe.Write(writer, document, Options);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class Probe : CollectionJsonConverter<ResourceListDocument<Widget>, Widget>
    {
        public JsonArray? Decorated { get; private set; }

        protected override void Decorate(JsonArray resources, ResourceListDocument<Widget> value) =>
            Decorated = resources;
    }

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
