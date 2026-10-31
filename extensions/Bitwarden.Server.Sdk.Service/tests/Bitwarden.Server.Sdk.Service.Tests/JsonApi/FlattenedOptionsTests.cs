using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Service.JsonApi;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// The options a document converter serialises through: the caller's own, less the document
/// converter, built once per caller-supplied instance.
/// </summary>
public sealed class FlattenedOptionsTests
{
    [Fact]
    public void For_RemovesTheDocumentConverter_KeepsTheRest()
    {
        var enums = new JsonStringEnumConverter();
        var source = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { enums, new ResourceDocumentJsonConverterFactory(), new ResourceDocumentJsonConverterFactory() },
        };

        var actual = FlattenedOptions.For(source);

        Assert.Same(enums, Assert.Single(actual.Converters));
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, actual.DefaultIgnoreCondition);
        Assert.Equal(JsonNamingPolicy.CamelCase, actual.PropertyNamingPolicy);
    }

    [Fact]
    public void For_LeavesTheCallersOptionsAlone()
    {
        var source = new JsonSerializerOptions { Converters = { new ResourceDocumentJsonConverterFactory() } };

        FlattenedOptions.For(source);

        Assert.IsType<ResourceDocumentJsonConverterFactory>(Assert.Single(source.Converters));
    }

    // A copy per call would rebuild System.Text.Json's metadata cache every time.
    [Fact]
    public void For_SameOptions_SameCopy()
    {
        var source = new JsonSerializerOptions();

        Assert.Same(FlattenedOptions.For(source), FlattenedOptions.For(source));
        Assert.NotSame(source, FlattenedOptions.For(source));
    }

    [Fact]
    public void For_DifferentOptions_DifferentCopies()
    {
        Assert.NotSame(FlattenedOptions.For(new JsonSerializerOptions()), FlattenedOptions.For(new JsonSerializerOptions()));
    }
}
