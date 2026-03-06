using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.HealthChecks.Tests;

public class AdhocHealthCheckTests
{
    [Fact]
    public async Task CanAddAndRemoveDegradedStatus()
    {
        using var host = CreateHost();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var client = host.GetTestClient();

        var reporter = host.Services.GetRequiredService<IHealthReporter>();

        var response = await client.GetAsync("healthz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var degradation = reporter.ReportDegradation("my-system", "Issue!");

        response = await client.GetAsync("healthz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        degradation.Dispose();

        response = await client.GetAsync("healthz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanReportIssuesDuringServiceConstruction()
    {
        using var host = CreateHost(services =>
        {
            var reporter = services.GetHealthReporter();
            reporter.ReportUnhealthy("startup", "you configured me wrong.");
        });

        await host.StartAsync(TestContext.Current.CancellationToken);

        var client = host.GetTestClient();

        var response = await client.GetAsync("healthz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnhealthyReportMakesHealthCheckUnhealth()
    {
        using var host = CreateHost();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var reporter = host.Services.GetRequiredService<IHealthReporter>();

        reporter.ReportDegradation("my-feature", "not good");
        reporter.ReportUnhealthy("my-other-feature", "really bad");
        reporter.ReportDegradation("another-feature", "not ideal");

        var client = host.GetTestClient();

        var response = await client.GetAsync("healthz", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetHealthReporter_CalledBeforeAddBitwardenHealthCheck_Fails()
    {
        var reached = false;
        using var host = CreateHostCore(services =>
        {
            var ex = Assert.Throws<InvalidOperationException>(() => services.GetHealthReporter());
            Assert.Contains("must call AddBitwardenHealthChecks", ex.Message);
            reached = true;
        });

        await host.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(reached);
    }

    [Fact]
    public void AddBitwardenHealthChecks_CalledMultipleTimes_NoAdditionalServices()
    {
        var services = new ServiceCollection();
        services.AddBitwardenHealthChecks();

        var expectedCount = services.Count;

        services.AddBitwardenHealthChecks();

        var actualCount = services.Count;

        Assert.Equal(expectedCount, actualCount);
    }

    private static IHost CreateHost(Action<IServiceCollection>? configureService = null)
    {
        return CreateHostCore(services =>
        {
            services.AddBitwardenHealthChecks();
            configureService?.Invoke(services);
        },
        endpoints =>
        {
            endpoints.MapHealthChecks("/healthz");
        });
    }

    private static IHost CreateHostCore(Action<IServiceCollection>? configureServices = null, Action<IEndpointRouteBuilder>? configureEndpoints = null)
    {
        return new HostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureServices(services =>
            {
                services.AddRouting();
                configureServices?.Invoke(services);
            })
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            configureEndpoints?.Invoke(endpoints);
                        });
                    });
            })
            .Build();
    }
}
