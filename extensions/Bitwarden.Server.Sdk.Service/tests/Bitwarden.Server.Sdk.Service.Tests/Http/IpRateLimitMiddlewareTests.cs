using System.Net;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.AspNetCore.Http;
using static Bitwarden.Server.Sdk.Service.Tests.Http.IpRateLimitingTests;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// The middleware hands a request within its limits on, and answers one over them itself, so nothing after it runs.
/// </summary>
public sealed class IpRateLimitMiddlewareTests
{
    [Fact]
    public async Task WithinTheLimit_TheNextRuns()
    {
        var limits = Limits(new IpRateLimitOptions { GeneralRules = [Rule("*", "1m", 1)] });
        var ran = 0;
        var middleware = new IpRateLimitMiddleware(_ => { ran++; return Task.CompletedTask; }, limits);

        var http = Request();
        await middleware.InvokeAsync(http);

        Assert.Equal(1, ran);
        Assert.Equal(200, http.Response.StatusCode);
    }

    [Fact]
    public async Task OverTheLimit_TheNextNeverRuns_AndTheRefusalIsAnswered()
    {
        var limits = Limits(new IpRateLimitOptions { GeneralRules = [Rule("*", "1m", 0)], HttpStatusCode = 503 });
        var ran = false;
        var middleware = new IpRateLimitMiddleware(_ => { ran = true; return Task.CompletedTask; }, limits);

        var http = Request();
        await middleware.InvokeAsync(http);

        Assert.False(ran);
        Assert.Equal(503, http.Response.StatusCode);
        Assert.Equal("60", http.Response.Headers.RetryAfter.ToString());
    }

    // Turned off, nothing is counted: a rule allowing none still lets every request through.
    [Fact]
    public async Task Disabled_TheNextAlwaysRuns()
    {
        var limits = Limits(new IpRateLimitOptions { Enabled = false, GeneralRules = [Rule("*", "1m", 0)] });
        var ran = 0;
        var middleware = new IpRateLimitMiddleware(_ => { ran++; return Task.CompletedTask; }, limits);

        await middleware.InvokeAsync(Request());
        await middleware.InvokeAsync(Request());

        Assert.Equal(2, ran);
    }

    private static DefaultHttpContext Request()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = "/a";
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        http.Response.Body = new MemoryStream();

        return http;
    }
}
