using System.Net;
using System.Net.Http.Json;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Bitwarden.Server.Sdk.UnitTests.Features;

public class FeatureCheckMiddlewareTests
{
    private readonly IProblemDetailsService _fakeProblemDetailsService;
    private readonly IHostEnvironment _fakeHostEnvironment;

    public FeatureCheckMiddlewareTests()
    {
        _fakeProblemDetailsService = Substitute.For<IProblemDetailsService>();
        _fakeHostEnvironment = Substitute.For<IHostEnvironment>();
    }

    [Fact]
    public async Task NoEndpointInvokesPipeline()
    {
        bool pipelineInvoked = false;
        var middleware = new FeatureCheckMiddleware(hc =>
        {
            pipelineInvoked = true;
            return Task.CompletedTask;
        }, _fakeHostEnvironment, _fakeProblemDetailsService, NullLogger<FeatureCheckMiddleware>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(null);

        var featureService = Substitute.For<IFeatureService>();

        await middleware.Invoke(httpContext, featureService);

        Assert.True(pipelineInvoked);
    }

    public static IEnumerable<object[]> HasMetadataData()
    {
        yield return Row([new RequireFeatureAttribute("configured-true")], StatusCodes.Status200OK);
        yield return Row([new RequireFeatureAttribute("configured-false")], StatusCodes.Status404NotFound);
        yield return Row([new RequireFeatureAttribute("configured-true"), new RequireFeatureAttribute("configured-false")], StatusCodes.Status404NotFound);
        yield return Row([new RequireFeatureAttribute("configured-true"), new RequireFeatureAttribute("configured-true")], StatusCodes.Status200OK);
        yield return Row([new RequireFeatureAttribute("configured-false"), new RequireFeatureAttribute("configured-false")], StatusCodes.Status404NotFound);
        yield return Row([], StatusCodes.Status200OK);
        yield return Row([new RequireFeatureAttribute("not-configured")], StatusCodes.Status404NotFound);

        static object[] Row(IFeatureMetadata[] featureMetadata, int expectedStatusCode)
        {
            return [featureMetadata, expectedStatusCode];
        }
    }

    [Theory]
    [MemberData(nameof(HasMetadataData))]
    public async Task HasMetadata_AllMustBeTrue(object[] metadata, int expectedStatusCode)
    {
        var middleware = new FeatureCheckMiddleware(hc =>
        {
            hc.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        }, _fakeHostEnvironment, _fakeProblemDetailsService, NullLogger<FeatureCheckMiddleware>.Instance);

        var context = GetContext(metadata);

        var featureService = Substitute.For<IFeatureService>();

        featureService.IsEnabled(Arg.Any<string>()).Returns(false);
        featureService.IsEnabled("configured-true").Returns(true);
        featureService.IsEnabled("configured-false").Returns(false);

        await middleware.Invoke(context, featureService);

        Assert.Equal(expectedStatusCode, context.Response.StatusCode);
    }

    [Fact]
    public async Task FailedCheck_ReturnsProblemDetails()
    {
        using var host = CreateHost();

        await host.StartAsync();

        var server = host.GetTestServer();
        var client = server.CreateClient();

        var response = await client.GetAsync("/require-feature");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("Resource not found.", problemDetails.Title);
    }

    [Fact]
    public async Task NoCheck_CallsEndpoint()
    {
        using var host = CreateHost();

        await host.StartAsync();

        var server = host.GetTestServer();
        var client = server.CreateClient();

        var response = await client.GetAsync("/no-feature-requirement");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var successResponse = await response.Content.ReadFromJsonAsync<SuccessResponse>();
        Assert.NotNull(successResponse);
        Assert.True(successResponse.Success);
    }

    [Fact]
    public async Task SuccessfulCheck_CallsEndpoint()
    {
        using var host = CreateHost(services =>
        {
            services.AddFeatureFlagValues(
                [
                    KeyValuePair.Create("my-feature", "true"),
                ]
            );
        });

        await host.StartAsync();

        var server = host.GetTestServer();
        var client = server.CreateClient();

        var response = await client.GetAsync("/require-feature");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var successResponse = await response.Content.ReadFromJsonAsync<SuccessResponse>();
        Assert.NotNull(successResponse);
        Assert.True(successResponse.Success);
    }

    private static DefaultHttpContext GetContext(params object[] metadata)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "TestEndpoint"));
        return httpContext;
    }

    record SuccessResponse(bool Success = true);

    private static IHost CreateHost(Action<IServiceCollection>? configureServices = null)
    {
        return new HostBuilder()
            .UseEnvironment("Development") // To get easier to read logs
            .ConfigureWebHost((webHostBuilder) =>
            {
                webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();

                        // We will manually add configuration later so this being empty is fine
                        services.AddFeatureFlagServices(new ConfigurationBuilder().Build());

                        configureServices?.Invoke(services);
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();

                        app.UseFeatureFlagChecks();

                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/require-feature", () => new SuccessResponse())
                                .RequireFeature("my-feature");

                            endpoints.MapGet("/no-feature-requirement", () => new SuccessResponse());
                        });
                    });
            })
            .Build();
    }
}
