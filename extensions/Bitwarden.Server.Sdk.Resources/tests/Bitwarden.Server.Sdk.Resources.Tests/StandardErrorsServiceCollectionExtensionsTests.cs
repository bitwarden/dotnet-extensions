using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Resources.Tests;

public sealed class StandardErrorsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddStandardErrors_RegistersTheExceptionHandler()
    {
        var services = new ServiceCollection().AddStandardErrors();

        var handler = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExceptionHandler));
        Assert.Equal(typeof(ErrorResponseExceptionHandler), handler.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, handler.Lifetime);
    }

    // A host and a package that both turn the standard errors on still answer through one handler.
    [Fact]
    public void AddStandardErrors_CalledTwice_RegistersTheHandlerOnce()
    {
        var services = new ServiceCollection().AddStandardErrors().AddStandardErrors();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IExceptionHandler));
    }
}
