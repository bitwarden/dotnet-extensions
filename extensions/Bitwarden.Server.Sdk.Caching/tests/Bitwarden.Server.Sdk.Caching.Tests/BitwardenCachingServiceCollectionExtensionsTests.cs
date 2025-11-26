using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.Caching.Tests;

public class BitwardenCachingServiceCollectionExtensionsTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _redis;

    public BitwardenCachingServiceCollectionExtensionsTests(RedisFixture redis)
    {
        _redis = redis;
    }

    [Fact]
    public void AddBitwardenCaching_MakesSeperateFusionInstancesAvailableViaKeyedService()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", _redis.Hostname},
        });

        var cacheOne = provider.GetRequiredKeyedService<IFusionCache>("CacheOne");
        var cacheTwo = provider.GetRequiredKeyedService<IFusionCache>("CacheTwo");

        Assert.NotSame(cacheOne, cacheTwo);
    }

    [Fact]
    public async Task AddBitwardenCaching_ValueRoundTrips()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", _redis.Hostname},
        });

        var cache = provider.GetRequiredKeyedService<IFusionCache>("MyFeature");
        await cache.SetAsync("Value", true, token: TestContext.Current.CancellationToken);
        var value = await cache.GetOrSetAsync("Value", false, token: TestContext.Current.CancellationToken);
        Assert.True(value);
    }

    [Fact]
    public async Task AddBitwardenCaching_LaterRegisteredItemWins()
    {
        var services = Create(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", _redis.Hostname},
        });

        services.AddBitwardenCaching();

        var fakeCache = new FakeDistributedCache();
        services.TryAddKeyedSingleton<IDistributedCache>("MyFeature", fakeCache);

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("MyFeature");
        await cache.SetAsync("Value", true, token: TestContext.Current.CancellationToken);

        // TODO: Validate how this key got built
        Assert.True(fakeCache.UnderlyingCache.ContainsKey("v2:MyFeature:Value"));
    }

    [Fact]
    public async Task AddBitwardenCaching_EarlierRegisteredItemWins()
    {
        var services = Create(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", _redis.Hostname},
        });

        var fakeCache = new FakeDistributedCache();
        services.TryAddKeyedSingleton<IDistributedCache>("MyFeature", fakeCache);

        services.AddBitwardenCaching();

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("MyFeature");
        await cache.SetAsync("Value", true, token: TestContext.Current.CancellationToken);

        Assert.True(fakeCache.UnderlyingCache.ContainsKey("v2:MyFeature:Value"));
    }

    [Fact]
    public void AddBitwardenCaching_AppliesDefaults()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", "main_redis"},
            {"Caching:Fusion:DefaultEntryOptions:SkipDistributedCacheRead", "true"},
            {"Caching:Fusion:DefaultEntryOptions:SkipDistributedCacheWrite", "false"},
            // No customization of "One", it should get defaults
            // "Two" customizations
            {"Caching:Uses:Two:Redis:Configuration", "custom_redis"},
            {"Caching:Uses:Two:Fusion:DefaultEntryOptions:SkipDistributedCacheRead", "false"},
            {"Caching:Uses:Two:Fusion:DefaultEntryOptions:SkipDistributedCacheWrite", "true"},
            {"Caching:Uses:Two:Fusion:DefaultEntryOptions:Duration", "00:01:00"},
            // "Three" customizations
            {"Caching:Uses:Three:Redis:Configuration", null},
        });

        var options = provider.GetRequiredService<IOptionsMonitor<CacheOptions>>();

        var one = options.Get("One");
        Assert.Equal("main_redis", one.Redis.Configuration);
        Assert.True(one.Fusion.DefaultEntryOptions.SkipDistributedCacheRead);
        Assert.False(one.Fusion.DefaultEntryOptions.SkipDistributedCacheWrite);
        Assert.Equal(FusionCacheGlobalDefaults.EntryOptionsDuration, one.Fusion.DefaultEntryOptions.Duration);

        var two = options.Get("Two");
        Assert.Equal("custom_redis", two.Redis.Configuration);
        Assert.False(two.Fusion.DefaultEntryOptions.SkipDistributedCacheRead);
        Assert.True(two.Fusion.DefaultEntryOptions.SkipDistributedCacheWrite);
        Assert.Equal(TimeSpan.FromMinutes(1), two.Fusion.DefaultEntryOptions.Duration);

        var three = options.Get("Three");
        Assert.Null(three.Redis.Configuration);
    }

    private static ServiceCollection Create(Dictionary<string, string?> initialData)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddProvider(new XUnitLoggerProvider(TestContext.Current.TestOutputHelper!));
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(initialData)
            .Build();

        services.AddSingleton<IConfiguration>(config);

        return services;
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> initialData)
    {
        var services = Create(initialData);
        services.AddBitwardenCaching();
        return services.BuildServiceProvider();
    }

    public class FakeDistributedCache : IDistributedCache
    {
        public Dictionary<string, byte[]> UnderlyingCache { get; set; } = [];
        public byte[]? Get(string key)
        {
            _ = UnderlyingCache.TryGetValue(key, out var bytes);
            return bytes;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            return Task.FromResult(Get(key));
        }

        public void Refresh(string key)
        {
            // No-op
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => UnderlyingCache.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            UnderlyingCache.Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            UnderlyingCache[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            UnderlyingCache[key] = value;
            return Task.CompletedTask;
        }
    }

    public sealed class XUnitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public XUnitLoggerProvider(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }
        public ILogger CreateLogger(string categoryName) => new XUnitLogger(categoryName, _testOutputHelper);
        public void Dispose() { }

        private sealed class XUnitLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly ITestOutputHelper _testOutputHelper;

            public XUnitLogger(string categoryName, ITestOutputHelper testOutputHelper)
            {
                _categoryName = categoryName;
                _testOutputHelper = testOutputHelper;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _testOutputHelper.WriteLine($"[{_categoryName}][{logLevel}] {formatter(state, exception)} {exception?.ToString()}");
            }
        }
    }
}
