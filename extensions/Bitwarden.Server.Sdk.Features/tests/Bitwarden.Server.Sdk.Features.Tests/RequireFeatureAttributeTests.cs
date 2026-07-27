using System.Net;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.UnitTests.Features;

public class RequireFeatureAttributeTests
{
    [Fact]
    public async Task ControllerClass_FeatureDisabled_Returns404()
    {
        using var host = CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/controller-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ControllerClass_FeatureEnabled_Returns200()
    {
        using var host = CreateHost(services =>
            services.AddFeatureFlagValues([KeyValuePair.Create("controller-feature", "true")]));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/controller-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ControllerAction_FeatureDisabled_Returns404()
    {
        using var host = CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/action-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ControllerAction_FeatureEnabled_Returns200()
    {
        using var host = CreateHost(services =>
            services.AddFeatureFlagValues([KeyValuePair.Create("action-feature", "true")]));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/action-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ControllerAction_SiblingActionWithoutAttribute_Accessible()
    {
        using var host = CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/ungated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MinimalApiDelegate_FeatureDisabled_Returns404()
    {
        using var host = CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/delegate-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MinimalApiDelegate_FeatureEnabled_Returns200()
    {
        using var host = CreateHost(services =>
            services.AddFeatureFlagValues([KeyValuePair.Create("delegate-feature", "true")]));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/delegate-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousDelegate_FeatureDisabled_Returns404()
    {
        using var host = CreateHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/lambda-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousDelegate_FeatureEnabled_Returns200()
    {
        using var host = CreateHost(services =>
            services.AddFeatureFlagValues([KeyValuePair.Create("lambda-feature", "true")]));
        await host.StartAsync(TestContext.Current.CancellationToken);

        var response = await host.GetTestClient().GetAsync("/lambda-gated", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [RequireFeature("delegate-feature")]
    private static Ok DelegateHandler() => TypedResults.Ok();

    private static IHost CreateHost(Action<IServiceCollection>? configureServices = null)
    {
        return new HostBuilder()
            .UseEnvironment("Development")
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder
                    .UseTestServer()
                    .ConfigureServices((_, services) =>
                    {
                        services.AddRouting();
                        services.AddControllers()
                            .AddApplicationPart(typeof(RequireFeatureAttributeTests).Assembly);
                        services.AddFeatureFlagServices();
                        configureServices?.Invoke(services);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseFeatureFlagChecks();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                            endpoints.MapGet("/delegate-gated", DelegateHandler);
                            endpoints.MapGet("/lambda-gated", [RequireFeature("lambda-feature")] () => TypedResults.Ok());
                        });
                    });
            })
            .Build();
    }
}

[RequireFeature("controller-feature")]
[Route("/controller-gated")]
public class FeatureGatedController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok();
}

[Route("")]
public class MixedController : ControllerBase
{
    [RequireFeature("action-feature")]
    [HttpGet("/action-gated")]
    public IActionResult GatedAction() => Ok();

    [HttpGet("/ungated")]
    public IActionResult UngatedAction() => Ok();
}
