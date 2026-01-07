using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;
using ZiggyCreatures.Caching.Fusion.Locking;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace Bitwarden.Server.Sdk.Caching.Tests;

public class BitwardenCachingServiceCollectionExtensionsTests : IClassFixture<RedisFixture>
{
    private readonly RedisFixture _redis;

    public BitwardenCachingServiceCollectionExtensionsTests(RedisFixture redis)
    {
        _redis = redis;
    }

    [Fact]
    public void MakesSeparateFusionInstancesAvailableViaKeyedService()
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
    public async Task ValueRoundTrips()
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
    public async Task LaterRegisteredItemWins()
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

        Assert.True(fakeCache.UnderlyingCache.ContainsKey("v2:MyFeature:Value"));
    }

    [Fact]
    public async Task EarlierRegisteredItemWins()
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
    public void AppliesDefaults()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", "main_redis"},
            {"Caching:Redis:InstanceName", "main_instance"},
            // No customization of "One", it should get defaults
            // "Two" customizations
            {"Caching:Uses:Two:Redis:Configuration", "custom_redis"},
            {"Caching:Uses:Two:Redis:InstanceName", "two_instance"},
            // "Three" customizations
            {"Caching:Uses:Three:Redis:Configuration", null},
            {"Caching:Uses:Three:Redis:InstanceName", null},
        });

        var redisOptions = provider.GetRequiredService<IOptionsMonitor<RedisCacheOptions>>();

        var redisOne = redisOptions.Get("One");
        Assert.Equal("main_redis", redisOne.Configuration);
        Assert.Equal("main_instance", redisOne.InstanceName);

        var redisTwo = redisOptions.Get("Two");
        Assert.Equal("custom_redis", redisTwo.Configuration);
        Assert.Equal("two_instance", redisTwo.InstanceName);

        var redisThree = redisOptions.Get("Three");
        Assert.Null(redisThree.Configuration);
        Assert.Null(redisThree.InstanceName);
    }

    [Fact]
    public async Task OneDistributedCache_Isolated()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var distributedCache = new FakeDistributedCache();
        services.AddSingleton<IDistributedCache>(distributedCache);

        var provider = services.BuildServiceProvider();

        var one = provider.GetRequiredKeyedService<IFusionCache>("One");
        var two = provider.GetRequiredKeyedService<IFusionCache>("Two");

        await one.SetAsync("Key", true, token: TestContext.Current.CancellationToken);
        await two.SetAsync("Key", false, token: TestContext.Current.CancellationToken);

        Assert.Collection(
            distributedCache.UnderlyingCache,
            (e) => Assert.Equal("v2:One:Key", e.Key),
            (e) => Assert.Equal("v2:Two:Key", e.Key)
        );

        Assert.True(await one.GetOrDefaultAsync("Key", false, token: TestContext.Current.CancellationToken));
        Assert.False(await two.GetOrDefaultAsync("Key", true, token: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CanConfigureCacheKeyPrefix()
    {
        var services = Create([]);

        services.AddBitwardenCaching();
        services.AddOptions<FusionCacheOptions>("One")
            .PostConfigure(options =>
            {
                options.CacheKeyPrefix = "custom/";
            });

        var distributedCache = new FakeDistributedCache();
        services.AddKeyedSingleton<IDistributedCache>("One", distributedCache);

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("One");

        await cache.SetAsync("MyKey", "", token: TestContext.Current.CancellationToken);
        Assert.Contains("v2:custom/MyKey", distributedCache.UnderlyingCache);
    }

    [Fact]
    public async Task RedisNotConfigured_NoDistributedCacheAdded_Throws()
    {
        // If redis is not being used for the distributed cache then we require that the host
        // add an alternate `IDistributedCache`, server will do this by registering IDistributedCache`
        var provider = CreateProvider([]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IFusionCache>("Test")
        );
        Assert.Equal("Redis was not configured for 'Test' and no non-keyed IDistributedCache was registered.", ex.Message);
    }

    [Fact]
    public async Task RedisNotConfigured_NonKeyedDistributedCacheAdded_Works()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var distributedCache = new FakeDistributedCache();
        services.AddSingleton<IDistributedCache>(distributedCache);

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("Test");

        await cache.SetAsync("Key", "Value", token: TestContext.Current.CancellationToken);
        Assert.Contains("v2:Test:Key", distributedCache.UnderlyingCache);
    }

    [Fact]
    public async Task NoRedisConfigured_CustomDistributed_NoBackplane_Works()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var distributedCache = new FakeDistributedCache();
        services.AddKeyedSingleton<IDistributedCache>("One", distributedCache);

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredKeyedService<IFusionCache>("One");

        await cache.SetAsync("Key", "Value", token: TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(1, "System.Int32")]
    [InlineData(4f, "System.Single")]
    [InlineData(1d, "System.Double")]
    [InlineData(new int[]{ 1 }, "System.Int32[]")]
    public void InvalidKey_Throws(object invalidKey, string typeName)
    {
        var provider = CreateProvider([]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IFusionCache>(invalidKey)
        );
        Assert.Contains("not valid", ex.Message);
        Assert.Contains(typeName, ex.Message);
    }

    [Fact]
    public void NullKey_Throws()
    {
        var provider = CreateProvider([]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IFusionCache>(null)
        );
        Assert.Contains("No service for type 'ZiggyCreatures.Caching.Fusion.IFusionCache' has been registered", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void EmptyStrings_Throws(string name)
    {
        var provider = CreateProvider([]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IFusionCache>(name)
        );
        Assert.Contains("Key for IFusionCache", ex.Message);
        Assert.Contains("whitespace", ex.Message);
    }

    [Fact]
    public void AnyKey_Throws()
    {
        var provider = CreateProvider([]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IFusionCache>(KeyedService.AnyKey)
        );
        Assert.Contains("not valid", ex.Message);
        Assert.Contains("AnyKey", ex.Message);
    }

    [Fact]
    public async Task CustomSerializer_IsUsed()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var serializer = Substitute.For<IFusionCacheSerializer>();
        services.AddKeyedSingleton("Test", serializer);

        var distributedCache = new FakeDistributedCache();
        services.AddKeyedSingleton<IDistributedCache>("Test", distributedCache);

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("Test");

        await cache.SetAsync("Key", "Value", token: TestContext.Current.CancellationToken);

        await serializer
            .Received(1)
            .SerializeAsync(Arg.Is<FusionCacheDistributedEntry<string>>(e => e.Value == "Value"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoCustomSerializer_ValueRoundTripsAsJson()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var distributedCache = new FakeDistributedCache();
        services.AddKeyedSingleton<IDistributedCache>("Test", distributedCache);

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("Test");

        await cache.SetAsync("Key", new { Message = "Hello!" }, token: TestContext.Current.CancellationToken);

        _ = JsonSerializer.Deserialize<JsonObject>(distributedCache.UnderlyingCache["v2:Test:Key"].Data);
    }

    [Fact]
    public async Task CustomMemoryLocker_IsUsed()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var memoryLocker = Substitute.For<IFusionCacheMemoryLocker>();
        services.AddKeyedSingleton("Test", memoryLocker);

        var distributedCache = new FakeDistributedCache();
        services.AddKeyedSingleton<IDistributedCache>("Test", distributedCache);

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("Test");

        await cache.GetOrSetAsync("Key", "Value", token: TestContext.Current.CancellationToken);

        await memoryLocker
            .Received(1)
            .AcquireLockAsync(
                cacheName: "Test",
                cacheInstanceId: Arg.Any<string>(),
                operationId: Arg.Any<string>(),
                key: "Test:Key",
                timeout: Arg.Any<TimeSpan>(),
                logger: Arg.Any<ILogger>(),
                token: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MultipleInstances_Works()
    {
        var providerOne = CreateProvider(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", _redis.Hostname},
        });

        var providerTwo = CreateProvider(new Dictionary<string, string?>
        {
            {"Caching:Redis:Configuration", _redis.Hostname},
        });

        var fusionOne = providerOne.GetRequiredKeyedService<IFusionCache>("Test");
        var fusionTwo = providerTwo.GetRequiredKeyedService<IFusionCache>("Test");

        await fusionOne.SetAsync("Key", true, token: TestContext.Current.CancellationToken);
        var value = await fusionTwo.GetOrDefaultAsync("Key", false, token: TestContext.Current.CancellationToken);
        Assert.True(value);

        // Setup a task that completes once the second instance recieves a message through its backplane
        var tcs = new TaskCompletionSource();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (!Debugger.IsAttached)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(2));
        }

        fusionTwo.Events.Backplane.MessageReceived += async (sender, args) =>
        {
            if (args.Message.Action != BackplaneMessageAction.EntrySet || args.Message.CacheKey != "Test:Key")
            {
                return;
            }

            tcs.TrySetResult();
        };

        await fusionOne.SetAsync("Key", false, token: cts.Token);

        // Wait for the second instance to receive the message the the first instance had a value change
        await tcs.Task;
        // Give it just a moment to set the value to the memory store, I could change this to wait for that event if this
        // proves to be flakey.
        await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);

        value = await fusionTwo.GetOrDefaultAsync("Key", true, token: cts.Token);

        Assert.False(value);
    }

    [Fact]
    public async Task CustomMemoryOptions_Works()
    {
        var services = Create([]);

        var distributedCache = new FakeDistributedCache();
        services.AddKeyedSingleton<IDistributedCache>("Test", distributedCache);

        services.AddBitwardenCaching();
        services.AddOptions<MemoryCacheOptions>("Test")
            .PostConfigure(options =>
            {
                options.TrackStatistics = true;
            });

        var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredKeyedService<IFusionCache>("Test");

        await cache.SetAsync("Key1", true, token: TestContext.Current.CancellationToken);

        var memoryCache = (MemoryCache)provider.GetRequiredKeyedService<IMemoryCache>("Test");

        var statistics = memoryCache.GetCurrentStatistics();

        Assert.NotNull(statistics);
        Assert.Equal(1, statistics.CurrentEntryCount);
    }

    [Fact]
    public async Task CustomLogCategories_Work()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var distributedCache = new FakeDistributedCache();
        services.AddKeyedSingleton<IDistributedCache>("CustomName", distributedCache);

        var provider = services.BuildServiceProvider();

        var cache = provider.GetRequiredKeyedService<IFusionCache>("CustomName");

        await cache.SetAsync("Key", "Value", token: TestContext.Current.CancellationToken);

        var logs = provider
            .GetFakeLogCollector()
            .GetSnapshot();

        Assert.Contains(logs, (l) => l.Category == "Bitwarden.Server.Sdk.Caching.CustomName");
    }

    [Fact]
    public async Task CalledMutlipleTimes_NoAdditionalServices()
    {
        var services = Create([]);

        services.AddBitwardenCaching();

        var baselineServicesCount = services.Count;

        services.AddBitwardenCaching();

        Assert.Equal(baselineServicesCount, services.Count);
    }

    private static ServiceCollection Create(Dictionary<string, string?> initialData)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.AddProvider(new XUnitLoggerProvider(TestContext.Current.TestOutputHelper!));
            logging.AddFakeLogging();
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
        public Dictionary<string, (byte[] Data, DistributedCacheEntryOptions Options)> UnderlyingCache { get; } = [];

        public byte[]? Get(string key)
        {
            _ = UnderlyingCache.TryGetValue(key, out var value);
            return value.Data;
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
            UnderlyingCache[key] = (value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            UnderlyingCache[key] = (value, options);
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
