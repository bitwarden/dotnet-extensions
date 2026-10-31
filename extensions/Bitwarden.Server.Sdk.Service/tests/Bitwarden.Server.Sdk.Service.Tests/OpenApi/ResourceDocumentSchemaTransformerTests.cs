using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// Each resource document, which its converter leaves opaque, is published in its wire shape:
/// the resource or a list of them under <c>data</c>, and a <c>meta</c> naming what the platform
/// puts there for that kind of document, with links on an offset-paged one only.
/// </summary>
public sealed class ResourceDocumentSchemaTransformerTests
{
    private const string Collection = "api/v1/gadgets";

    /// <summary>

    /// The resource the documents carry.

    /// </summary>
    public sealed record Gadget : Resource
    {
        /// <summary>
        /// An attribute.
        /// </summary>
        public string? Label { get; init; }
    }

    [Fact]
    public async Task TransformAsync_SingleDocument_CarriesTheResource()
    {
        var document = await GenerateAsync();

        var schema = Schema(document, "GadgetDocument");
        Assert.Equal(JsonSchemaType.Object, schema.Type);
        Assert.Equal("Gadget", Assert.IsType<OpenApiSchemaReference>(schema.Properties!["data"]).Reference.Id);
        Assert.Equal(["data"], schema.Required!);
        Assert.False(schema.Properties.ContainsKey("links"));

        var meta = Assert.IsType<OpenApiSchema>(schema.Properties["meta"]);
        Assert.Equal(JsonSchemaType.Object | JsonSchemaType.Null, meta.Type);
        Assert.True(meta.ReadOnly);
        Assert.NotNull(meta.AdditionalProperties);
        Assert.Null(meta.Properties);
    }

    [Fact]
    public async Task TransformAsync_UnpagedList_CarriesAnArrayAndNamesNoMeta()
    {
        var document = await GenerateAsync();

        var schema = Schema(document, "GadgetListDocument");
        var data = Assert.IsType<OpenApiSchema>(schema.Properties!["data"]);
        Assert.Equal(JsonSchemaType.Array, data.Type);
        Assert.Equal("Gadget", Assert.IsType<OpenApiSchemaReference>(data.Items).Reference.Id);
        Assert.Null(Assert.IsType<OpenApiSchema>(schema.Properties["meta"]).Properties);
        Assert.False(schema.Properties.ContainsKey("links"));
    }

    [Fact]
    public async Task TransformAsync_OffsetPagedList_NamesTheTotalsAndLinks()
    {
        var document = await GenerateAsync();

        var schema = Schema(document, "GadgetPagedListDocument");
        Assert.Equal(JsonSchemaType.Array, Assert.IsType<OpenApiSchema>(schema.Properties!["data"]).Type);

        var meta = Schema(document, "PageMeta");
        Assert.Equal(["pageCount", "pageNumber", "pageSize", "totalCount"], meta.Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["pageCount", "pageNumber", "pageSize", "totalCount"], meta.Required!.Order(StringComparer.Ordinal));

        var links = Assert.IsType<OpenApiSchema>(schema.Properties["links"]);
        Assert.True(links.ReadOnly);
        Assert.Equal(["first", "last", "next", "prev"], links.Properties!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TransformAsync_CursorPagedList_NamesTheCursor()
    {
        var document = await GenerateAsync();

        var schema = Schema(document, "GadgetCursorListDocument");
        Assert.Equal(JsonSchemaType.Array, Assert.IsType<OpenApiSchema>(schema.Properties!["data"]).Type);
        Assert.False(schema.Properties.ContainsKey("links"));

        var meta = Schema(document, "CursorPageMeta");
        Assert.Equal(["cursor", "hasMore", "pageSize"], meta.Properties!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["hasMore", "pageSize"], meta.Required!.Order(StringComparer.Ordinal));
        Assert.Equal(JsonSchemaType.Boolean, ((OpenApiSchema)meta.Properties["hasMore"]).Type);
    }

    [Fact]
    public async Task TransformAsync_BulkCreate_CarriesTheResourcesAndDescribesLid()
    {
        var document = await GenerateAsync();

        var schema = RequestBody(document, $"/{Collection}:bulk-create");
        Assert.Equal(["data"], schema.Properties!.Keys);
        Assert.Equal(["data"], schema.Required!);

        var data = Assert.IsType<OpenApiSchema>(schema.Properties["data"]);
        Assert.Equal(JsonSchemaType.Array, data.Type);
        Assert.Equal("Gadget", Assert.IsType<OpenApiSchemaReference>(data.Items).Reference.Id);
        Assert.Contains("\"lid\"", data.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransformAsync_BulkReplace_CarriesTheResourcesAlone()
    {
        var document = await GenerateAsync();

        var schema = RequestBody(document, $"/{Collection}:bulk-replace");
        Assert.Equal(["data"], schema.Properties!.Keys);

        var data = Assert.IsType<OpenApiSchema>(schema.Properties["data"]);
        Assert.Equal(JsonSchemaType.Array, data.Type);
        Assert.Null(data.Description);
    }

    [Fact]
    public async Task TransformAsync_BulkCreated_CountsAndEchoesLid()
    {
        var document = await GenerateAsync();

        var schema = Schema(document, "GadgetBulkCreatedDocument");
        Assert.Equal(["data", "meta"], schema.Required!.Order(StringComparer.Ordinal));
        Assert.Contains("\"lid\"", Assert.IsType<OpenApiSchema>(schema.Properties!["data"]).Description, StringComparison.Ordinal);

        var meta = Assert.IsType<OpenApiSchema>(schema.Properties["meta"]);
        Assert.True(meta.ReadOnly);
        Assert.Equal(["affectedCount"], meta.Properties!.Keys);
        Assert.Equal(["affectedCount"], meta.Required!);
    }

    // Returned before the generator is asked for anything, so a context with no document behind it is enough.
    [Theory]
    [InlineData(typeof(Gadget))]
    [InlineData(typeof(List<Gadget>))]
    public async Task TransformAsync_NotADocument_IsLeftAlone(Type type)
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Array };

        await new ResourceDocumentSchemaTransformer().TransformAsync(schema, new OpenApiSchemaTransformerContext
        {
            DocumentName = "v1",
            JsonTypeInfo = JsonSerializerOptions.Web.GetTypeInfo(type),
            JsonPropertyInfo = null,
            ParameterDescription = null,
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);

        Assert.Equal(JsonSchemaType.Array, schema.Type);
        Assert.Null(schema.Properties);
    }

    private static OpenApiSchema Schema(OpenApiDocument document, string name) =>
        Assert.IsType<OpenApiSchema>(document.Components!.Schemas![name]);

    private static OpenApiSchema RequestBody(OpenApiDocument document, string path)
    {
        var body = document.Paths[path].Operations![HttpMethod.Post].RequestBody!.Content!["application/json"].Schema;

        return body is OpenApiSchemaReference reference ? Schema(document, reference.Reference.Id!) : Assert.IsType<OpenApiSchema>(body);
    }

    private static async Task<OpenApiDocument> GenerateAsync()
    {
        await using var app = new Documented().Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Documented() : Service("documents-probe", "Documents Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }

        protected override void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            var gadgets = endpoints.MapGroup(Collection).WithStandards();
            gadgets.MapGet("{id}", GadgetsEndpoints.GetGadget);
            gadgets.MapGet("", GadgetsEndpoints.ListGadgets);

            endpoints.MapGroup("api/v1/gadget-names").WithStandards().MapGet("", GadgetsEndpoints.ListGadgetNames);
            endpoints.MapGroup("api/v1/gadget-events").WithStandards().MapGet("", GadgetsEndpoints.ListGadgetEvents);

            endpoints.MapPost($"{Collection}:bulk-create", GadgetsEndpoints.CreateGadgets).WithStandards();
            endpoints.MapPost($"{Collection}:bulk-replace", GadgetsEndpoints.ReplaceGadgets).WithStandards();
        }
    }

    /// <summary>
    /// One operation per kind of document.
    /// </summary>
    [ApiTag(Description = "Gadgets, described for the tests.")]
    public static class GadgetsEndpoints
    {
        /// <summary>
        /// Read a gadget
        /// </summary>
        [StandardReadOne]
        public static ResourceDocument<Gadget> GetGadget(string id) => StandardResponses.ReadOneResponse(new Gadget { Id = id });

        /// <summary>
        /// List gadgets
        /// </summary>
        [StandardReadMany]
        public static PagedResourceListDocument<Gadget> ListGadgets(PageNumber pageNumber = default, PageSize pageSize = default) =>
            throw new NotSupportedException();

        /// <summary>
        /// List every gadget
        /// </summary>
        [StandardReadMany]
        public static ResourceListDocument<Gadget> ListGadgetNames() => throw new NotSupportedException();

        /// <summary>
        /// List gadget events
        /// </summary>
        [StandardReadMany]
        public static CursorPagedResourceListDocument<Gadget> ListGadgetEvents(PageSize pageSize = default, PageAfter pageAfter = default) =>
            throw new NotSupportedException();

        /// <summary>
        /// Create gadgets
        /// </summary>
        [StandardBulkCreate(Collection)]
        public static BulkCreatedDocument<Gadget> CreateGadgets(BulkCreate<Gadget> request) => throw new NotSupportedException();

        /// <summary>
        /// Replace gadgets
        /// </summary>
        [StandardBulkReplace(Collection)]
        public static BulkResult ReplaceGadgets(BulkReplace<Gadget> request) => throw new NotSupportedException();
    }
}
