using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// Every paged document's <c>meta</c> refers to one shared <c>PageMeta</c> schema, and every
/// cursor-paged one to <c>CursorPageMeta</c>, rather than each carrying its own inline object.
/// </summary>
public sealed class PageMetaDocumentTransformerTests
{
    [Fact]
    public async Task TransformAsync_PagedDocuments_ShareOneMeta()
    {
        var meta = new OpenApiSchema { Type = JsonSchemaType.Object, Description = "offset" };
        var document = Document(
            ("WidgetPagedListDocument", Paged(meta)),
            ("FolderPagedListDocument", Paged(new OpenApiSchema())));

        await Transform(document);

        Assert.Same(meta, document.Components!.Schemas!["PageMeta"]);
        foreach (var name in new[] { "WidgetPagedListDocument", "FolderPagedListDocument" })
        {
            var reference = Assert.IsType<OpenApiSchemaReference>(Meta(document, name));
            Assert.Equal("PageMeta", reference.Reference.Id);
        }
    }

    [Fact]
    public async Task TransformAsync_CursorDocuments_ShareTheirOwnMeta()
    {
        var offset = new OpenApiSchema { Description = "offset" };
        var cursor = new OpenApiSchema { Description = "cursor" };
        var document = Document(
            ("WidgetPagedListDocument", Paged(offset)),
            ("EventCursorListDocument", Paged(cursor)));

        await Transform(document);

        Assert.Same(offset, document.Components!.Schemas!["PageMeta"]);
        Assert.Same(cursor, document.Components.Schemas["CursorPageMeta"]);
        Assert.Equal("CursorPageMeta", Assert.IsType<OpenApiSchemaReference>(Meta(document, "EventCursorListDocument")).Reference.Id);
    }

    [Fact]
    public async Task TransformAsync_OtherDocuments_AreLeftAlone()
    {
        var meta = new OpenApiSchema();
        var document = Document(
            ("WidgetListDocument", Paged(meta)),
            ("WidgetPagedListDocument", new OpenApiSchema { Properties = new Dictionary<string, IOpenApiSchema>() }));

        await Transform(document);

        Assert.Same(meta, Meta(document, "WidgetListDocument"));
        Assert.False(document.Components!.Schemas!.ContainsKey("PageMeta"));
        Assert.False(document.Components.Schemas.ContainsKey("CursorPageMeta"));
    }

    [Fact]
    public async Task TransformAsync_NoSchemas_IsLeftAlone()
    {
        var document = new OpenApiDocument();

        await Transform(document);

        Assert.Null(document.Components);
    }

    private static OpenApiSchema Paged(IOpenApiSchema meta) => new()
    {
        Type = JsonSchemaType.Object,
        Properties = new Dictionary<string, IOpenApiSchema> { ["data"] = new OpenApiSchema(), ["meta"] = meta },
    };

    private static IOpenApiSchema Meta(OpenApiDocument document, string schema) =>
        ((OpenApiSchema)document.Components!.Schemas![schema]).Properties!["meta"];

    private static OpenApiDocument Document(params (string Name, OpenApiSchema Schema)[] schemas) => new()
    {
        Components = new OpenApiComponents
        {
            Schemas = schemas.ToDictionary(schema => schema.Name, IOpenApiSchema (schema) => schema.Schema),
        },
    };

    private static Task Transform(OpenApiDocument document) =>
        new PageMetaDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
