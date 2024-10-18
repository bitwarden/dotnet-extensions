using Bitwarden.Extensions.Hosting.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Bitwarden.Extensions.Hosting.Tests.Features;

public class FeatureApplicationBuilderExtensionsTests
{
    [Fact]
    public async Task UseFeatureChecks_RegistersMiddleware()
    {
        // Arrange
        var featureService = Substitute.For<IFeatureService>();
        var services = CreateServices(featureService);

        var app = new ApplicationBuilder(services);

        app.UseFeatureChecks();

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

    private IServiceProvider CreateServices(IFeatureService featureService)
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
