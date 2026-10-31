using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bitwarden.Server.Sdk.Service.Tests;

public sealed class SystemEndpointsTests
{
    [Fact]
    public async Task Readiness_NothingRegistered_Healthy()
    {
        var actual = await Readiness(_ => { });

        var ok = Assert.IsType<Ok<SystemStatus>>(actual.Result);
        Assert.Equal(SystemStatus.Healthy, ok.Value);
    }

    [Fact]
    public async Task Readiness_EveryCheckHealthyOrDegraded_Healthy()
    {
        var actual = await Readiness(checks => checks
            .AddCheck("healthy", () => HealthCheckResult.Healthy())
            .AddCheck("degraded", () => HealthCheckResult.Degraded("Slow.")));

        Assert.IsType<Ok<SystemStatus>>(actual.Result);
    }

    [Fact]
    public async Task Readiness_ACheckUnhealthy_ServiceUnavailableNamingWhat()
    {
        var actual = await Readiness(checks => checks
            .AddCheck("healthy", () => HealthCheckResult.Healthy())
            .AddCheck("queue", () => HealthCheckResult.Unhealthy("The 'q' subscription receives nothing.")));

        var unavailable = Assert.IsType<JsonHttpResult<SystemStatus>>(actual.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("UNHEALTHY", unavailable.Value?.Status);
        Assert.Equal("The 'q' subscription receives nothing.", unavailable.Value?.Detail);
    }

    [Fact]
    public async Task Readiness_ACheckThrows_ServiceUnavailableWithoutTheExceptionText()
    {
        var actual = await Readiness(checks => checks
            .AddCheck("broker", () => throw new InvalidOperationException("amqp://broker.internal:5672 refused")));

        var unavailable = Assert.IsType<JsonHttpResult<SystemStatus>>(actual.Result);
        Assert.Equal("A dependency is not ready.", unavailable.Value?.Detail);
    }

    private static async Task<Results<Ok<SystemStatus>, JsonHttpResult<SystemStatus>>> Readiness(
        Action<IHealthChecksBuilder> configure)
    {
        var services = new ServiceCollection().AddLogging();
        configure(services.AddHealthChecks());
        await using var provider = services.BuildServiceProvider();

        return await SystemEndpoints.Readiness(
            provider.GetRequiredService<HealthCheckService>(), TestContext.Current.CancellationToken);
    }
}
