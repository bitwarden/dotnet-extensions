using System.Net;
using System.Reflection;
using Bitwarden.Server.Sdk.Service.Http;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using static Bitwarden.Server.Sdk.Service.Tests.Http.IpRateLimitingTests;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// A Redis of its own for the shared per-address counts.
/// </summary>
public sealed class RateLimitRedisFixture : IAsyncLifetime
{
    private const int InnerRedisPort = 6379;

    public IContainer Container { get; } = new ContainerBuilder()
        .WithImage("redis:alpine")
        .WithPortBinding(InnerRedisPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
        .Build();

    public string ConnectionString => $"{Container.Hostname}:{Container.GetMappedPublicPort(InnerRedisPort)}";

    public async ValueTask InitializeAsync() => await Container.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await Container.DisposeAsync();
}

// Distributed IP rate limiting (globalSettings:distributedIpRateLimiting): with a Redis, every
// instance of a service counts an address against one allowance; without one, each process counts on
// its own; while Redis is away, limiting is skipped, and no request is refused for Redis's sake.
public sealed class RateLimitCountersTests(RateLimitRedisFixture redis) : IClassFixture<RateLimitRedisFixture>
{
    // Two instances of one service draw on one allowance: what one counted, the other sees.
    [Fact]
    public async Task TwoInstances_ShareOneAllowance()
    {
        var service = $"svc-{Guid.NewGuid():N}";
        using var first = Counters(service);
        using var second = Counters(service);
        var a = Limits(Options(Rule("post:*", "1m", 2)), counters: first);
        var b = Limits(Options(Rule("post:*", "1m", 2)), counters: second);

        Assert.Equal(200, await Send(a, "POST", "/a"));
        Assert.Equal(200, await Send(b, "POST", "/a"));
        Assert.Equal(429, await Send(a, "POST", "/a"));
        Assert.Equal(429, await Send(b, "POST", "/a"));

        // Another address keeps its own allowance.
        Assert.Equal(200, await Send(b, "POST", "/a", IPAddress.Parse("198.51.100.77")));
    }

    // A window opens with its first request and lasts the rule's period.
    [Fact]
    public async Task AWindow_ClosesAfterItsPeriod()
    {
        using var counters = Counters($"svc-{Guid.NewGuid():N}");

        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.False(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));

        await Task.Delay(TimeSpan.FromMilliseconds(1300), TestContext.Current.CancellationToken);

        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    // Two services carrying a rule for the same route (a proxy in front of Identity, and Identity) each
    // count a request once, not each other's too.
    [Fact]
    public async Task TwoServices_CountApart()
    {
        using var proxy = Counters($"proxy-{Guid.NewGuid():N}");
        using var identity = Counters($"identity-{Guid.NewGuid():N}");

        Assert.True(await proxy.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        Assert.True(await identity.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
    }

    // Redis holds the address only inside a hash of what is counted, and every key expires.
    [Fact]
    public async Task Redis_HoldsNoAddress_AndEveryKeyExpires()
    {
        var service = $"svc-{Guid.NewGuid():N}";
        using var counters = Counters(service);
        var limits = Limits(Options(Rule("post:*", "1h", 5)), counters: counters);

        Assert.Equal(200, await Send(limits, "POST", "/a", IPAddress.Parse("192.0.2.200")));

        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var server = connection.GetServer(connection.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"{service}:*").ToList();

        var key = Assert.Single(keys);
        Assert.DoesNotContain("192.0.2.200", key.ToString(), StringComparison.Ordinal);
        Assert.Matches($"^{service}:ip-rate-limit:[0-9a-f]{{64}}$", key.ToString());
        var ttl = await connection.GetDatabase().KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1));
    }

    // Limiting is skipped while Redis is away: every request is let through.
    [Fact]
    public async Task RedisAway_SkipsLimiting()
    {
        using var counters = new RedisRateLimitCounters(
            RedisRateLimitCounters.Connect("127.0.0.1:1,connectTimeout=200"),
            "svc",
            new DistributedIpRateLimitingOptions(),
            TimeProvider.System,
            NullLogger<RedisRateLimitCounters>.Instance);
        var limits = Limits(Options(Rule("post:*", "1m", 1)), counters: counters);

        Assert.Equal(200, await Send(limits, "POST", "/a"));
        Assert.Equal(200, await Send(limits, "POST", "/a"));
    }

    // The threshold: by default 10 failures within 120 seconds set Redis aside until the window closes.
    [Fact]
    public async Task EnoughFailures_SetRedisAside_UntilTheWindowCloses()
    {
        var time = new ManualTime();
        var failing = FailingRedis.Create();
        using var counters = new RedisRateLimitCounters(
            () => Task.FromResult<IConnectionMultiplexer>(failing),
            "svc",
            new DistributedIpRateLimitingOptions { MaxRedisTimeoutsThreshold = 3, SlidingWindowSeconds = 120 },
            time,
            NullLogger<RedisRateLimitCounters>.Instance);

        // One bucket with a limit of 1: every request is let through, failing or set aside.
        for (var i = 0; i < 5; i++)
        {
            Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        }

        Assert.Equal(3, ((FailingRedis)(object)failing).Calls);

        time.Advance(TimeSpan.FromSeconds(121));
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
        Assert.Equal(4, ((FailingRedis)(object)failing).Calls);
    }

    // The store: Redis given a connection string and enabled; this process's memory otherwise.
    [Theory]
    [InlineData(null, null, typeof(MemoryRateLimitCounters))]
    [InlineData("localhost:6379", "false", typeof(MemoryRateLimitCounters))]
    [InlineData("localhost:6379", null, typeof(RedisRateLimitCounters))]
    public void TheStore_IsRedisOnlyWhenConfiguredAndEnabled(string? connectionString, string? enabled, Type expected)
    {
        var settings = new Dictionary<string, string?>
        {
            ["globalSettings:distributedIpRateLimiting:redisConnectionString"] = connectionString,
            ["globalSettings:distributedIpRateLimiting:enabled"] = enabled,
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Where(setting => setting.Value is not null))
            .Build();
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(new Environment())
            .AddLogging()
            .AddIpRateLimiting()
            .BuildServiceProvider();

        Assert.IsType(expected, provider.GetRequiredService<IRateLimitCounters>());
    }

    private RedisRateLimitCounters Counters(string service) => new(
        RedisRateLimitCounters.Connect(redis.ConnectionString),
        service,
        new DistributedIpRateLimitingOptions(),
        TimeProvider.System,
        NullLogger<RedisRateLimitCounters>.Instance);

    private static IpRateLimitOptions Options(params IpRateLimitRule[] rules) => new() { GeneralRules = [.. rules] };

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    /// <summary>
    /// A connected Redis whose every script times out.
    /// </summary>
    public class FailingRedis : DispatchProxy
    {
        public int Calls;

        public static IConnectionMultiplexer Create()
        {
            var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, FailingRedis>();
            return multiplexer;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_IsConnected":
                    return true;
                case "GetDatabase":
                    var database = DispatchProxy.Create<IDatabase, FailingDatabase>();
                    ((FailingDatabase)(object)database).Owner = this;
                    return database;
                case "Dispose":
                    return null;
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    public class FailingDatabase : DispatchProxy
    {
        public FailingRedis Owner = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "ScriptEvaluateAsync")
            {
                Interlocked.Increment(ref Owner.Calls);
                return Task.FromException<RedisResult>(new RedisTimeoutException("Timeout", CommandStatus.Sent));
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
