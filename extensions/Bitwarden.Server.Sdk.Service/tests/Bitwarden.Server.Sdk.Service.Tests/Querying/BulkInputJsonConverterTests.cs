using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.Querying;

/// <summary>
/// A bulk input on the wire: an object is the input for every resource, an array one input per
/// resource, and it is written back in the shape it was read.
/// </summary>
public sealed class BulkInputJsonConverterTests
{
    [Fact]
    public void Read_Object_IsEvery()
    {
        var actual = JsonSerializer.Deserialize<BulkInput<Move>>("""{ "folder": "f" }""", JsonSerializerOptions.Web)!;

        Assert.Equal("f", actual.Every!.Folder);
        Assert.Null(actual.Each);
    }

    [Fact]
    public void Read_Array_IsEach()
    {
        var actual = JsonSerializer.Deserialize<BulkInput<Move>>("""[{ "folder": "f" }, { "folder": "g" }]""", JsonSerializerOptions.Web)!;

        Assert.Equal(["f", "g"], actual.Each!.Select(move => move.Folder));
        Assert.Null(actual.Every);
    }

    [Fact]
    public void Read_EmptyArray_IsAnEmptyEach()
    {
        var actual = JsonSerializer.Deserialize<BulkInput<Move>>("[]", JsonSerializerOptions.Web)!;

        Assert.Empty(actual.Each!);
    }

    [Fact]
    public void Read_Null_IsNull()
    {
        Assert.Null(JsonSerializer.Deserialize<BulkInput<Move>>("null", JsonSerializerOptions.Web));
    }

    // Reached when the converter is asked directly, as a converter wrapping this one may.
    [Fact]
    public void Read_NullToken_IsNull()
    {
        var reader = new Utf8JsonReader("null"u8);
        reader.Read();

        Assert.Null(new BulkInputJsonConverter<Move>().Read(ref reader, typeof(BulkInput<Move>), JsonSerializerOptions.Web));
    }

    [Fact]
    public void Read_AScalar_IsEvery()
    {
        Assert.Equal(3, JsonSerializer.Deserialize<BulkInput<int>>("3", JsonSerializerOptions.Web)!.Every);
    }

    [Fact]
    public void Write_Every_IsTheObject()
    {
        var actual = JsonSerializer.Serialize(new BulkInput<Move> { Every = new Move("f") }, JsonSerializerOptions.Web);

        Assert.Equal("""{"folder":"f"}""", actual);
    }

    [Fact]
    public void Write_Each_IsTheArray()
    {
        var actual = JsonSerializer.Serialize(new BulkInput<Move> { Each = [new Move("f"), new Move("g")] }, JsonSerializerOptions.Web);

        Assert.Equal("""[{"folder":"f"},{"folder":"g"}]""", actual);
    }

    public sealed record Move(string Folder);
}
