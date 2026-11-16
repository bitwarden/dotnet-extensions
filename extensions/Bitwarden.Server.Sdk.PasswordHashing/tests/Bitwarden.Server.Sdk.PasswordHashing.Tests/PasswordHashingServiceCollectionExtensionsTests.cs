using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.PasswordHashing.Tests;

public sealed class PasswordHashingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddServerPasswordHasher_RegistersSingleton()
    {
        using var services = new ServiceCollection().AddServerPasswordHasher().BuildServiceProvider();
        using var scope = services.CreateScope();

        var hasher = services.GetRequiredService<IServerPasswordHasher>();

        Assert.Same(hasher, scope.ServiceProvider.GetRequiredService<IServerPasswordHasher>());
    }

    [Fact]
    public void AddServerPasswordHasher_CalledTwice_RegistersOnce()
    {
        var services = new ServiceCollection().AddServerPasswordHasher().AddServerPasswordHasher();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IServerPasswordHasher));
    }

    [Fact]
    public void AddServerPasswordHasher_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PasswordHashingServiceCollectionExtensions.AddServerPasswordHasher(null!));
    }
}
