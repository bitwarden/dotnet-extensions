using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A Redis with the shelf service's own user, as a deployment provisions one (<see cref="ServiceCacheAcl.User(string, string, IEnumerable{string})"/>),
/// beside the default user the tests inspect it with.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private const int InnerRedisPort = 6379;

    // A test's own password, never a deployment's.
    private const string ServicePassword = "shelf-service-test-password";

    public RedisFixture()
    {
        Container = new ContainerBuilder()
            .WithImage("redis")
            .WithPortBinding(InnerRedisPort, true)
            .WithCommand(
            [
                "redis-server",
                // A user directive's rules are separate arguments on the command line.
                "--user", .. ServiceCacheAcl.User(ShelfHost.ServiceName, ServicePassword, reads: []).Split(' '),
            ])
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
    }

    public IContainer Container { get; }

    /// <summary>
    /// The connection as Redis's default user, which may do anything: for a test looking at what the service wrote.
    /// </summary>
    public string Hostname
    {
        get => $"{Container.Hostname}:{Container.GetMappedPublicPort(InnerRedisPort)}";
    }

    /// <summary>
    /// The connection as the shelf service's own user, as the service's host is configured outside Development.
    /// </summary>
    public string Service => $"{Hostname},user={ShelfHost.ServiceName},password={ServicePassword}";

    public async ValueTask InitializeAsync()
    {
        await Container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
