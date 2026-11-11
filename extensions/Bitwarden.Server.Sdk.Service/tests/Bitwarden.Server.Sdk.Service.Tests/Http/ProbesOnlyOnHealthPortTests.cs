using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// The health port answers the probes, each at its exact path, and nothing else; any other port answers everything.
/// </summary>
public sealed class ProbesOnlyOnHealthPortTests
{
    private const int HealthPort = 8081;

    [Theory]
    [InlineData("/api/v1/health")]
    [InlineData("/api/v1/readiness")]
    [InlineData("/API/V1/STARTUP")]
    public async Task HealthPort_AProbe_IsAnswered(string path) =>
        Assert.Equal(StatusCodes.Status200OK, await Send(HealthPort, path));

    // Exactly the probe's path: one beneath it, or beside it, is not a probe.
    [Theory]
    [InlineData("/api/v1/health/anything")]
    [InlineData("/api/v1/healthy")]
    [InlineData("/api/v1/probes/current")]
    [InlineData("/")]
    public async Task HealthPort_AnythingButAProbe_Is404(string path) =>
        Assert.Equal(StatusCodes.Status404NotFound, await Send(HealthPort, path));

    [Fact]
    public async Task AnotherPort_AnythingIsAnswered() =>
        Assert.Equal(StatusCodes.Status200OK, await Send(HealthPort + 1, "/api/v1/health/anything"));

    /// <summary>
    /// The status a request on <paramref name="port"/> is answered with, where whatever passes the filter answers 200.
    /// </summary>
    private static async Task<int> Send(int port, string path)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        new ProbesOnlyOnHealthPort(HealthPort).Configure(next => next.Run(http =>
        {
            http.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }))(app);

        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = path;
        http.Connection.LocalPort = port;
        http.Response.StatusCode = 0;

        await app.Build()(http);

        return http.Response.StatusCode;
    }
}
