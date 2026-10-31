using Bitwarden.Server.Sdk.Service.Endpoints;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// The <c>sort</c> parameter is published as a string naming the fields the returned resource
/// may be sorted by and the order used without one, after whatever the author wrote, and says so
/// when the collection cannot be sorted.
/// </summary>
public sealed class SortingDocumentTransformerTests
{
    /// <summary>
    /// A resource sortable by two fields, newest rank first by default.
    /// </summary>
    public sealed record Ranked : Resource
    {
        /// <summary>
        /// Sortable.
        /// </summary>
        [Sortable]
        public string? Label { get; init; }

        /// <summary>

        /// Sortable, and the default, descending.

        /// </summary>
        [Sortable(Default = true, Descending = true)]
        public int Rank { get; init; }
    }

    /// <summary>
    /// A resource declaring nothing sortable.
    /// </summary>
    public sealed record Unsorted : Resource;

    [Fact]
    public async Task TransformAsync_Sort_NamesTheFieldsAndTheDefault()
    {
        var operation = await Transform(nameof(Handlers.ListRanked), new OpenApiParameter { Name = "sort", Required = false });

        var sort = Assert.Single(operation.Parameters!);
        Assert.Equal("sort", sort.Name);
        Assert.Equal(ParameterLocation.Query, sort.In);
        Assert.False(sort.Required);
        Assert.Equal(JsonSchemaType.String, sort.Schema!.Type);
        Assert.Equal("-rank", sort.Schema.Default!.GetValue<string>());
        Assert.Equal(
            "Comma-delimited, in order of precedence; prefix a field with `-` to sort it descending. "
            + "Sortable fields: `label`, `rank`. Defaults to `-rank`.",
            sort.Description);
    }

    [Fact]
    public async Task TransformAsync_AuthoredDescription_Leads()
    {
        var operation = await Transform(nameof(Handlers.ListRankedLater), new OpenApiParameter { Name = "order", Description = "The order to list in.  " });

        var sort = Assert.Single(operation.Parameters!);
        Assert.Equal("sort", sort.Name);
        Assert.StartsWith("The order to list in. Comma-delimited", sort.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "This collection cannot be sorted.")]
    [InlineData("Ignored here.", "Ignored here.")]
    public async Task TransformAsync_NothingSortable_SaysSo(string? authored, string expected)
    {
        var operation = await Transform(nameof(Handlers.ListUnsorted), new OpenApiParameter { Name = "sort", Description = authored });

        var sort = Assert.Single(operation.Parameters!);
        Assert.Equal(expected, sort.Description);
        Assert.Null(sort.Schema!.Default);
    }

    [Fact]
    public async Task TransformAsync_HandlerNotReturningResources_HasNothingSortable()
    {
        var operation = await Transform(nameof(Handlers.ListNothing), new OpenApiParameter { Name = "sort" });

        Assert.Equal("This collection cannot be sorted.", Assert.Single(operation.Parameters!).Description);
    }

    [Fact]
    public async Task TransformAsync_OtherParameters_AreLeftAlone()
    {
        var page = new OpenApiParameter { Name = "page[size]" };

        var operation = await Transform(nameof(Handlers.ListRanked), page);

        Assert.Same(page, Assert.Single(operation.Parameters!));
    }

    [Fact]
    public async Task TransformAsync_NoSort_IsLeftAlone()
    {
        var sort = new OpenApiParameter { Name = "sort" };

        var operation = await Transform(nameof(Handlers.Plain), sort);

        Assert.Same(sort, Assert.Single(operation.Parameters!));
    }

    [Fact]
    public async Task TransformAsync_NoParametersOrPaths_IsLeftAlone()
    {
        var operation = new OpenApiOperation();

        await Run(Document(operation), nameof(Handlers.ListRanked));
        await Run(new OpenApiDocument { Paths = null! }, nameof(Handlers.ListRanked));

        Assert.Null(operation.Parameters);
    }

    private static async Task<OpenApiOperation> Transform(string handler, params OpenApiParameter[] parameters)
    {
        var operation = new OpenApiOperation { Parameters = [.. parameters] };
        await Run(Document(operation), handler);
        return operation;
    }

    private static OpenApiDocument Document(OpenApiOperation operation) => new()
    {
        Paths = new OpenApiPaths
        {
            ["/api/v1/ranked"] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
            },
        },
    };

    private static Task Run(OpenApiDocument document, string handler)
    {
        var method = typeof(Handlers).GetMethod(handler)!;
        var description = new ApiDescription
        {
            HttpMethod = "GET",
            RelativePath = "api/v1/ranked",
            ActionDescriptor = new ActionDescriptor
            {
                EndpointMetadata = [method],
                Parameters = [.. method.GetParameters().Select(ParameterDescriptor (parameter) => new HandlerParameterDescriptor
                {
                    Name = parameter.Name!,
                    ParameterType = parameter.ParameterType,
                    ParameterInfo = parameter,
                })],
            },
        };

        return new SortingDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [new ApiDescriptionGroup("v1", [description])],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Reads taking a sort, and one that does not.
    /// </summary>
    public static class Handlers
    {
        /// <summary>
        /// A sortable collection.
        /// </summary>
        public static PagedResourceListDocument<Ranked> ListRanked(Sort? sort) => null!;

        /// <summary>

        /// A sortable collection, later, under another parameter name.

        /// </summary>
        public static Task<ResourceListDocument<Ranked>> ListRankedLater(Sort? order) => null!;

        /// <summary>

        /// A collection with nothing to sort by.

        /// </summary>
        public static ResourceListDocument<Unsorted> ListUnsorted(Sort? sort) => null!;

        /// <summary>

        /// Returns no resources.

        /// </summary>
        public static void ListNothing(Sort? sort)
        {
        }

        /// <summary>

        /// Takes no sort.

        /// </summary>
        public static ResourceListDocument<Ranked> Plain(string? sort) => null!;
    }
}
