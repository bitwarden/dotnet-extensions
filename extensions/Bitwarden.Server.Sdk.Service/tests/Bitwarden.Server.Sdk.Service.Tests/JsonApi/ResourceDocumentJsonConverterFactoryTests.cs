using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;

namespace Bitwarden.Server.Sdk.Service.Tests.JsonApi;

/// <summary>
/// Which types carry the <c>attributes</c> envelope, and the converter each is given.
/// </summary>
/// <remarks>
/// Only documents are converted, never a resource itself: a type with a converter reports no
/// properties, which would leave a resource's published schema opaque.
/// </remarks>
public sealed class ResourceDocumentJsonConverterFactoryTests
{
    private readonly ResourceDocumentJsonConverterFactory _factory = new();

    [Theory]
    [InlineData(typeof(ResourceDocument<Widget>), typeof(ResourceDocumentJsonConverter<Widget>))]
    [InlineData(typeof(ResourceListDocument<Widget>), typeof(ResourceListJsonConverter<Widget>))]
    [InlineData(typeof(PagedResourceListDocument<Widget>), typeof(PagedResourceListJsonConverter<Widget>))]
    [InlineData(typeof(CursorPagedResourceListDocument<Widget>), typeof(CursorPagedResourceListJsonConverter<Widget>))]
    [InlineData(typeof(BulkCreate<Widget>), typeof(BulkCreateJsonConverter<Widget>))]
    [InlineData(typeof(BulkCreatedDocument<Widget>), typeof(BulkCreatedJsonConverter<Widget>))]
    [InlineData(typeof(BulkReplace<Widget>), typeof(BulkReplaceJsonConverter<Widget>))]
    public void Document_ConvertedByItsOwnConverter(Type document, Type converter)
    {
        Assert.True(_factory.CanConvert(document));
        Assert.IsType(converter, _factory.CreateConverter(document, JsonSerializerOptions.Default));
    }

    [Theory]
    [InlineData(typeof(Widget))]
    [InlineData(typeof(Resource))]
    [InlineData(typeof(List<Widget>))]
    [InlineData(typeof(Dictionary<string, Widget>))]
    [InlineData(typeof(PageLinks))]
    [InlineData(typeof(string))]
    public void NotADocument_NotConverted(Type type)
    {
        Assert.False(_factory.CanConvert(type));
    }

    public sealed record Widget : Resource;
}
