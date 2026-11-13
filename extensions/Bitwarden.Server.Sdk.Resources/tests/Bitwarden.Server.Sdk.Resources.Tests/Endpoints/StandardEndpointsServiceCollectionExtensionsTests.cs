using Bitwarden.Server.Sdk.Resources.Endpoints;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Tests.Endpoints;

public sealed class StandardEndpointsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddStandardEndpoints_CalledTwice_RegistersEachServiceOnce()
    {
        var services = new ServiceCollection().AddStandardEndpoints().AddStandardEndpoints();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(StandardEndpointProblems));
        var provider = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IApiDescriptionProvider));
        Assert.Equal(typeof(StandardEndpointApiDescriptionProvider), provider.ImplementationType);
    }
}
