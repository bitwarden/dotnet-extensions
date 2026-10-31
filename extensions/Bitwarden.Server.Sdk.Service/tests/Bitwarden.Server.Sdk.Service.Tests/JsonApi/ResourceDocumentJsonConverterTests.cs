using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// Reading a single-resource document: the envelope comes off, and a resource must declare the
/// one <c>type</c> the document carries, refused by a pointer to it otherwise.
/// </summary>
public sealed class ResourceDocumentJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ResourceDocumentJsonConverterFactory() },
    };

    [Fact]
    public void Read_Enveloped_IdAndAttributesOnTheRecord()
    {
        var actual = Read("""{"data":{"id":"w-1","type":"widget","attributes":{"name":"fake"}}}""");

        Assert.Equal("w-1", actual.Data.Id);
        Assert.Equal("widget", actual.Data.Type);
        Assert.Equal("fake", actual.Data.Name);
    }

    // The specification forbids both; the outer keeps id and type authoritative.
    [Fact]
    public void Read_MemberInsideAndOutsideAttributes_OuterWins()
    {
        var actual = Read("""{"data":{"id":"w-1","type":"widget","attributes":{"id":"w-2","name":"fake"}}}""");

        Assert.Equal("w-1", actual.Data.Id);
    }

    [Fact]
    public void Read_NotAnObject_Refused()
    {
        var actual = Assert.Throws<JsonException>(() => Read("[1]"));

        Assert.Equal("A resource document must be a JSON object.", actual.Message);
    }

    [Theory]
    [InlineData("""{"data":{"attributes":{}}}""")]
    [InlineData("""{"data":{"type":7,"attributes":{}}}""")]
    [InlineData("""{"data":{"type":null,"attributes":{}}}""")]
    public void Read_TypeMissingOrNotAString_RefusedAtTheType(string body)
    {
        var actual = Assert.Throws<ResourceContractException>(() => Read(body));

        Assert.Equal("/data/type", actual.Pointer);
        Assert.Equal("A resource must declare \"type\": \"widget\".", actual.Message);
    }

    // Otherwise a caller could post a gadget and be told it created a widget.
    [Fact]
    public void Read_AnotherType_RefusedAtTheType()
    {
        var actual = Assert.Throws<ResourceContractException>(() => Read("""{"data":{"type":"gadget","attributes":{}}}"""));

        Assert.Equal("/data/type", actual.Pointer);
        Assert.Equal("A resource of type \"widget\" was expected, but \"gadget\" was declared.", actual.Message);
    }

    [Fact]
    public void Read_TypeOfDifferentCase_Refused()
    {
        Assert.Throws<ResourceContractException>(() => Read("""{"data":{"type":"Widget","attributes":{}}}"""));
    }

    [Fact]
    public void Read_NoData_Refused()
    {
        Assert.ThrowsAny<JsonException>(() => Read("{}"));
    }

    [Fact]
    public void Write_ThenRead_SameDocument()
    {
        var document = new ResourceDocument<Widget> { Data = new Widget { Id = "w-1", Name = "fake" } };

        var actual = Read(JsonSerializer.Serialize(document, Options));

        Assert.Equal(document.Data, actual.Data);
    }

    private static ResourceDocument<Widget> Read(string json) =>
        JsonSerializer.Deserialize<ResourceDocument<Widget>>(json, Options)!;

    public sealed record Widget : Resource
    {
        public string? Name { get; init; }
    }
}
