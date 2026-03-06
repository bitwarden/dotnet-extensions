using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Bitwarden.Server.Sdk.Caching.Tests;

public sealed class RedisFixture : IAsyncLifetime
{
    private const int InnerRedisPort = 6379;

    public RedisFixture()
    {
        Container = new ContainerBuilder()
            .WithImage("redis")
            .WithPortBinding(InnerRedisPort, true)
            .Build();
    }

    public IContainer Container { get; }

    public string Hostname
    {
        get => $"{Container.Hostname}:{Container.GetMappedPublicPort(InnerRedisPort)}";
    }

    public async ValueTask InitializeAsync()
    {
        await Container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
