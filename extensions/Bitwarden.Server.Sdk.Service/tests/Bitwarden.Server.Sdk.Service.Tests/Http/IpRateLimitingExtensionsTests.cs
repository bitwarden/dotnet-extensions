using System.Net;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Bitwarden.Server.Sdk.Service.Tests.Http.IpRateLimitingTests;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// <c>UseIpRateLimiting</c> puts the limits in the pipeline where it is called, ahead of what follows.
/// </summary>
public sealed class IpRateLimitingExtensionsTests
{
    [Fact]
    public async Task UseIpRateLimiting_RefusesBeforeWhatFollows()
    {
        var services = new ServiceCollection()
            .AddSingleton(Limits(new IpRateLimitOptions { GeneralRules = [Rule("*", "1m", 1)] }))
            .BuildServiceProvider();
        var answered = 0;
        var app = new ApplicationBuilder(services);
        app.UseIpRateLimiting();
        app.Run(_ =>
        {
            answered++;
            return Task.CompletedTask;
        });
        var pipeline = app.Build();

        var first = Request(services);
        await pipeline(first);
        var second = Request(services);
        await pipeline(second);

        Assert.Equal(1, answered);
        Assert.Equal(200, first.Response.StatusCode);
        Assert.Equal(429, second.Response.StatusCode);
    }

    private static DefaultHttpContext Request(IServiceProvider services)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = "GET";
        http.Request.Path = "/a";
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        http.Response.Body = new MemoryStream();

        return http;
    }
}
