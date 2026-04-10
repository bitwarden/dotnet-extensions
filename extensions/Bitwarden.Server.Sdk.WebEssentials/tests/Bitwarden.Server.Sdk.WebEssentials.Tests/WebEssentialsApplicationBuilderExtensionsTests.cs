using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.WebEssentials.Tests;

public class WebEssentialsApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task UseSecurityHeaders_Works()
    {
        using var app = BuildApp(() => Results.Ok("Hello"));
        await app.StartAsync(TestContext.Current.CancellationToken);

        var response = await app.GetTestClient().GetAsync("", TestContext.Current.CancellationToken);

        AssertSecurityHeadersPresent(response);
    }

    private static WebApplication BuildApp(Func<IResult> handler)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Services.AddRoutingCore();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.UseSecurityHeaders();
        app.MapGet("/", handler);

        return app;
    }

    private static void AssertSecurityHeadersPresent(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("x-frame-options", out var frameOptions));
        Assert.Equal("SAMEORIGIN", Assert.Single(frameOptions));

        Assert.True(response.Headers.TryGetValues("x-xss-protection", out var xssProtections));
        Assert.Equal("1; mode=block", Assert.Single(xssProtections));

        Assert.True(response.Headers.TryGetValues("x-content-type-options", out var contentTypeOptions));
        Assert.Equal("nosniff", Assert.Single(contentTypeOptions));
    }
}
