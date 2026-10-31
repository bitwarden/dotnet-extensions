using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// Every operation gets one <c>default</c> response referring to the shared error document, whose
/// nested error and source objects are published as named schemas rather than left inline.
/// </summary>
public sealed class StandardErrorResponseDocumentTransformerTests
{
    [Fact]
    public async Task TransformAsync_EveryOperation_FailsWithTheErrorDocument()
    {
        var document = await GenerateAsync();

        Assert.Contains("/api/v1/gauges/{id}", document.Paths.Keys);
        var operations = document.Paths.Values.SelectMany(path => path.Operations!.Values).ToList();
        Assert.True(operations.Count > 1);
        Assert.All(operations, operation =>
        {
            var failure = operation.Responses!["default"];
            Assert.Equal("The request did not succeed. The body describes why.", failure.Description);
            Assert.Equal("ErrorResponse", Assert.IsType<OpenApiSchemaReference>(failure.Content!["application/json"].Schema).Reference.Id);
        });
    }

    [Fact]
    public async Task TransformAsync_NestedObjects_AreNamedSchemas()
    {
        var document = await GenerateAsync();

        var schemas = document.Components!.Schemas!;
        var response = Assert.IsType<OpenApiSchema>(schemas["ErrorResponse"]);
        var errors = Assert.IsType<OpenApiSchema>(response.Properties!["errors"]);
        Assert.Equal("Error", Assert.IsType<OpenApiSchemaReference>(errors.Items).Reference.Id);

        var error = Assert.IsType<OpenApiSchema>(schemas["Error"]);
        Assert.Equal("ErrorSource", Assert.IsType<OpenApiSchemaReference>(error.Properties!["source"]).Reference.Id);
        Assert.IsType<OpenApiSchema>(schemas["ErrorSource"]);
    }

    // Returned before the generator is asked for anything, so a context with no document behind it is enough.
    [Fact]
    public async Task TransformAsync_NoPaths_AddsNothing()
    {
        var document = new OpenApiDocument { Paths = null! };

        await new StandardErrorResponseDocumentTransformer().TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = "v1",
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);

        Assert.Null(document.Components);
    }

    private static async Task<OpenApiDocument> GenerateAsync()
    {
        await using var app = new Documented().Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        return await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Documented() : Service("errors-probe", "Errors Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }

        protected override void MapEndpoints(IEndpointRouteBuilder endpoints) =>
            endpoints.MapGroup("api/v1/gauges").WithStandards().MapGet("{id}", GaugesEndpoints.GetGauge);
    }

    /// <summary>
    /// The resource the operation reads.
    /// </summary>
    public sealed record Gauge : Resource;

    /// <summary>
    /// One operation beside the platform's own.
    /// </summary>
    [ApiTag(Description = "Gauges, described for the tests.")]
    public static class GaugesEndpoints
    {
        /// <summary>
        /// Read a gauge
        /// </summary>
        [StandardReadOne]
        public static ResourceDocument<Gauge> GetGauge(string id) => StandardResponses.ReadOneResponse(new Gauge { Id = id });
    }
}
