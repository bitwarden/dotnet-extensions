using System.Text.Json;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// The platform's refusal body: a JSON error document naming the status already set, code
/// <c>too-many-requests</c>, and the sentence as its detail.
/// </summary>
public sealed class PlatformIpRateLimitRefusalTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task WriteAsync_TheErrorDocument_WithTheStatusAlreadySet(int status)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.Response.StatusCode = status;

        await new PlatformIpRateLimitRefusal().WriteAsync(http, "Slow down! Too many requests. Try again in 1m.");

        Assert.StartsWith("application/json", http.Response.ContentType, StringComparison.Ordinal);
        http.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        var error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), error.GetProperty("status").GetString());
        Assert.Equal("too-many-requests", error.GetProperty("code").GetString());
        Assert.Equal("Too many requests.", error.GetProperty("title").GetString());
        Assert.Equal("Slow down! Too many requests. Try again in 1m.", error.GetProperty("detail").GetString());
    }
}
