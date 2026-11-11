using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Bitwarden.Server.Sdk.Client.Tests;

/// <summary>
/// A Redis of its own for the tests of reading a service-owned cache.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private const int InnerRedisPort = 6379;

    public IContainer Container { get; } = new ContainerBuilder()
        .WithImage("redis")
        .WithPortBinding(InnerRedisPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
        .Build();

    public string Hostname => $"{Container.Hostname}:{Container.GetMappedPublicPort(InnerRedisPort)}";

    public async ValueTask InitializeAsync() => await Container.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}
