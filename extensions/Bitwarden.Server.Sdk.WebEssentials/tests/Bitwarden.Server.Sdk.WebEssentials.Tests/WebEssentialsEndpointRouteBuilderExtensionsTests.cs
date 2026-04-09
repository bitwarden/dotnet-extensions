using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.WebEssentials.Tests;

public class WebEssentialsEndpointRouteBuilderExtensionsTests
{
    [Fact]
    public async Task MapVersionEndpoint_ReturnsVersionJson()
    {
        using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);

        var response = await app.GetTestClient().GetAsync("/version", TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("version", out var versionElement));
        Assert.True(Version.TryParse(versionElement.GetString(), out _));
    }

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Services.AddRoutingCore();
        builder.Services.AddWebEssentials();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapVersionEndpoint();

        return app;
    }
}
