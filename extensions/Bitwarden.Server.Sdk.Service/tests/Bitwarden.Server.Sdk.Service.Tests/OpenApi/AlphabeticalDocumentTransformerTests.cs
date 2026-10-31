using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// The document's sections and paths come out in ordinal order whatever order the service mapped
/// them in, so the committed document is stable.
/// </summary>
public sealed class AlphabeticalDocumentTransformerTests
{
    [Fact]
    public async Task TransformAsync_Tags_AreInOrdinalOrder()
    {
        var document = new OpenApiDocument
        {
            Tags = new HashSet<OpenApiTag> { new() { Name = "Widgets" }, new() { Name = "Folders" }, new() { Name = "api keys" } },
        };

        await Transform(document);

        Assert.Equal(["Folders", "Widgets", "api keys"], document.Tags.Select(tag => tag.Name));
    }

    [Fact]
    public async Task TransformAsync_Paths_AreInOrdinalOrder()
    {
        var widgets = new OpenApiPathItem();
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/api/v1/widgets"] = widgets,
                ["/api/v1/folders/{id}"] = new OpenApiPathItem(),
                ["/api/v1/folders"] = new OpenApiPathItem(),
            },
        };

        await Transform(document);

        Assert.Equal(["/api/v1/folders", "/api/v1/folders/{id}", "/api/v1/widgets"], document.Paths.Keys);
        Assert.Same(widgets, document.Paths["/api/v1/widgets"]);
    }

    [Fact]
    public async Task TransformAsync_NothingToOrder_IsLeftAlone()
    {
        var document = new OpenApiDocument
        {
            Tags = new HashSet<OpenApiTag> { new() { Name = "Widgets" } },
            Paths = [],
        };

        await Transform(document);

        Assert.Equal(["Widgets"], document.Tags.Select(tag => tag.Name));
        Assert.Empty(document.Paths);
    }

    private static Task Transform(OpenApiDocument document) =>
        new AlphabeticalDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
}
