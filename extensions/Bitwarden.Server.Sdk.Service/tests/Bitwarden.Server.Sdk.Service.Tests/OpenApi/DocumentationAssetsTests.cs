using System.Net;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// The documentation's branding is served by the service itself, to a caller with no
/// credential, with the content type a browser needs, and is kept out of the API's contract.
/// </summary>
public sealed class DocumentationAssetsTests
{
    [Theory]
    [InlineData(DocumentationAssets.StylesheetPath, "text/css")]
    [InlineData(DocumentationAssets.LogoPath, "image/svg+xml")]
    [InlineData(DocumentationAssets.ShieldPath, "image/svg+xml")]
    [InlineData(DocumentationAssets.FaviconPath, "image/x-icon")]
    public async Task Get_Asset_IsServedAnonymously(string path, string contentType)
    {
        await using var app = new Branded().Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var response = await app.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Document_Assets_AreNotInTheContract()
    {
        await using var app = new Branded().Build();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(document.Paths.Keys, path => path.StartsWith("/docs/", StringComparison.Ordinal));
    }

    private sealed class Branded() : Service("branded-probe", "Branded Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }
    }
}
