using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching.Tests;

/// <summary>
/// A tombstone refuses a slow reader's old version only while it is there. A Redis that may evict keys may evict a
/// tombstone, and the old version is then written after all, so the owner fills nothing there and logs why.
/// </summary>
public sealed class RedisOwnedCacheStoreTests
{
    [Theory]
    [InlineData("0", "volatile-lru", true)]
    [InlineData("0", "allkeys-lfu", true)]
    [InlineData("104857600", "noeviction", true)]
    [InlineData("104857600", "volatile-lru", false)]
    [InlineData("104857600", "volatile-ttl", false)]
    [InlineData("104857600", "allkeys-random", false)]
    [InlineData("104857600", null, false)]
    [InlineData(null, null, false)]
    public void KeepsTombstones_OnlyWhereNothingIsEvicted(string? maxmemory, string? policy, bool expected) =>
        Assert.Equal(expected, RedisOwnedCacheStore.KeepsTombstones(maxmemory, policy));

    [Fact]
    public async Task Fill_RedisThatMayEvict_FillsNothingAndSaysWhy()
    {
        await using var redis = await StartAsync("--maxmemory", "100mb", "--maxmemory-policy", "volatile-lru");
        await using var connection = new OwnedCacheConnection(redis.Address);
        var logger = new FakeLogger<RedisOwnedCacheStore>();
        var store = new RedisOwnedCacheStore(connection, logger);
        var key = $"shelf-service:book:{Guid.NewGuid()}";

        Assert.False(await store.FillAsync(key, 1, null, "{}", TimeSpan.FromMinutes(1)));
        Assert.False(await store.FillAsync(key, 1, null, "{}", TimeSpan.FromMinutes(1)));

        await using var inspect = await ConnectionMultiplexer.ConnectAsync(redis.Address);
        Assert.False(await inspect.GetDatabase().KeyExistsAsync(key));
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Contains("volatile-lru", record.Message);
        Assert.Contains(RedisOwnedCacheStore.RequiredPolicy, record.Message);
    }

    // Invalidation carries on: an entry written before the policy changed is still tombstoned.
    [Fact]
    public async Task Invalidate_RedisThatMayEvict_StillTombstones()
    {
        await using var redis = await StartAsync("--maxmemory", "100mb", "--maxmemory-policy", "volatile-lru");
        await using var connection = new OwnedCacheConnection(redis.Address);
        var store = new RedisOwnedCacheStore(connection, new FakeLogger<RedisOwnedCacheStore>());
        var key = $"shelf-service:book:{Guid.NewGuid()}";

        await store.InvalidateAsync([new Invalidation(key, 2)], TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        await using var inspect = await ConnectionMultiplexer.ConnectAsync(redis.Address);
        Assert.Equal("1", (string?)await inspect.GetDatabase().HashGetAsync(key, OwnedCacheEntry.Gone));
    }

    [Fact]
    public async Task Fill_RedisThatNeverEvicts_Fills()
    {
        await using var redis = await StartAsync("--maxmemory", "100mb", "--maxmemory-policy", "noeviction");
        await using var connection = new OwnedCacheConnection(redis.Address);
        var logger = new FakeLogger<RedisOwnedCacheStore>();
        var store = new RedisOwnedCacheStore(connection, logger);

        Assert.True(await store.FillAsync($"shelf-service:book:{Guid.NewGuid()}", 1, null, "{}", TimeSpan.FromMinutes(1)));
        Assert.Empty(logger.Collector.GetSnapshot());
    }

    private static async Task<Redis> StartAsync(params string[] arguments)
    {
        var container = new ContainerBuilder()
            .WithImage("redis:7")
            .WithPortBinding(Redis.Port, true)
            .WithCommand(["redis-server", .. arguments])
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
        await container.StartAsync(TestContext.Current.CancellationToken);
        return new Redis(container);
    }

    private sealed class Redis(IContainer container) : IAsyncDisposable
    {
        public const int Port = 6379;

        public string Address => $"{container.Hostname}:{container.GetMappedPublicPort(Port)}";

        public ValueTask DisposeAsync() => container.DisposeAsync();
    }
}
