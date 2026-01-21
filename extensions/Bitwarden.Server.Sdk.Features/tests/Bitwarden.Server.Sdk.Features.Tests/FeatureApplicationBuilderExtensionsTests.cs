using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Bitwarden.Server.Sdk.Features;

public class FeatureApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task UseFeatureFlagChecks_RegistersMiddleware()
    {
        // Arrange
        var featureService = Substitute.For<IFeatureService>();
        var services = CreateServices(featureService);

        var app = new ApplicationBuilder(services);

        app.UseFeatureFlagChecks();

        var appFunc = app.Build();

        var endpoint = new Endpoint(
            null,
            new EndpointMetadataCollection(new RequireFeatureAttribute("test-attribute")),
            "Test endpoint");

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = services;
        httpContext.SetEndpoint(endpoint);

        // Act
        await appFunc(httpContext);

        // Assert
        featureService.Received(1).IsEnabled("test-attribute");
    }

    private static ServiceProvider CreateServices(IFeatureService featureService)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddProblemDetails(options => { });
        services.AddSingleton(Substitute.For<IHostEnvironment>());
        services.AddSingleton(featureService);

        var serviceProvder = services.BuildServiceProvider();

        return serviceProvder;
    }
}
