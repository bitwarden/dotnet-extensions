using System.Net;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// <c>AddIpRateLimiting</c> binds both sections, counts in Redis only when shared counting is enabled and has a
/// connection string, and leaves a service's own refusal and counts in place.
/// </summary>
public sealed class IpRateLimitingServiceCollectionExtensionsTests
{
    // The store: Redis given a connection string and enabled; this process's memory otherwise.
    [Theory]
    [InlineData(null, null, typeof(MemoryRateLimitCounters))]
    [InlineData(" ", null, typeof(MemoryRateLimitCounters))]
    [InlineData("localhost:6379", "false", typeof(MemoryRateLimitCounters))]
    [InlineData("localhost:6379", null, typeof(RedisRateLimitCounters))]
    public void TheStore_IsRedisOnlyWhenConfiguredAndEnabled(string? connectionString, string? enabled, Type expected)
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["globalSettings:distributedIpRateLimiting:redisConnectionString"] = connectionString,
            ["globalSettings:distributedIpRateLimiting:enabled"] = enabled,
        });

        Assert.IsType(expected, provider.GetRequiredService<IRateLimitCounters>());
    }

    // The in-memory store is the one registered as itself too, so a service's own limits can share it.
    [Fact]
    public void WithoutRedis_TheStoreIsTheSingleMemoryCounters()
    {
        using var provider = Provider([]);

        Assert.Same(provider.GetRequiredService<MemoryRateLimitCounters>(), provider.GetRequiredService<IRateLimitCounters>());
    }

    [Fact]
    public void BothSections_AreBound()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["IpRateLimitOptions:Enabled"] = "false",
            ["IpRateLimitOptions:HttpStatusCode"] = "503",
            ["IpRateLimitOptions:GeneralRules:0:Endpoint"] = "post:/connect/token",
            ["IpRateLimitOptions:GeneralRules:0:Period"] = "1m",
            ["IpRateLimitOptions:GeneralRules:0:Limit"] = "10",
            ["globalSettings:distributedIpRateLimiting:maxRedisTimeoutsThreshold"] = "3",
            ["globalSettings:distributedIpRateLimiting:slidingWindowSeconds"] = "30",
        });

        var limits = provider.GetRequiredService<IOptions<IpRateLimitOptions>>().Value;
        var distributed = provider.GetRequiredService<IOptions<DistributedIpRateLimitingOptions>>().Value;

        Assert.False(limits.Enabled);
        Assert.Equal(503, limits.HttpStatusCode);
        var rule = Assert.Single(limits.GeneralRules);
        Assert.Equal(("post:/connect/token", "1m", 10), (rule.Endpoint, rule.Period, rule.Limit));
        Assert.Equal(3, distributed.MaxRedisTimeoutsThreshold);
        Assert.Equal(30, distributed.SlidingWindowSeconds);
    }

    // A service registering its own refusal body, or its own store, keeps it.
    [Fact]
    public void AServicesOwnRefusalAndCounters_AreKept()
    {
        var refusal = new SilentRefusal();
        var counters = new MemoryRateLimitCounters(new FakeTimeProvider());
        using var provider = Provider([], services => services
            .AddSingleton<IIpRateLimitRefusal>(refusal)
            .AddSingleton<IRateLimitCounters>(counters));

        Assert.Same(refusal, provider.GetRequiredService<IIpRateLimitRefusal>());
        Assert.Same(counters, provider.GetRequiredService<IRateLimitCounters>());
    }

    [Fact]
    public void TheRefusal_IsThePlatformsByDefault()
    {
        using var provider = Provider([]);

        Assert.IsType<PlatformIpRateLimitRefusal>(provider.GetRequiredService<IIpRateLimitRefusal>());
    }

    // The in-memory windows open and close by the registered clock.
    [Fact]
    public async Task TheMemoryCounters_CountByTheRegisteredClock()
    {
        var time = new FakeTimeProvider();
        using var provider = Provider([], services => services.AddSingleton<TimeProvider>(time));
        var counters = provider.GetRequiredService<IRateLimitCounters>();
        var token = TestContext.Current.CancellationToken;

        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), token));
        Assert.False(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), token));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await counters.TryTakeAsync("bucket", 1, TimeSpan.FromMinutes(1), token));
    }

    // What the middleware needs resolves, and limits what the configuration says.
    [Fact]
    public async Task TheLimiter_Resolves_FromTheConfiguration()
    {
        using var provider = Provider(new Dictionary<string, string?>
        {
            ["IpRateLimitOptions:GeneralRules:0:Endpoint"] = "*",
            ["IpRateLimitOptions:GeneralRules:0:Period"] = "1m",
            ["IpRateLimitOptions:GeneralRules:0:Limit"] = "1",
        });
        var limits = provider.GetRequiredService<IpRateLimiting>();
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = "/a";
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");

        Assert.True(limits.Enabled);
        Assert.Null(await limits.TryTakeAsync(http));
        Assert.NotNull(await limits.TryTakeAsync(http));
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Where(setting => setting.Value is not null))
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(new Environment())
            .AddLogging();
        configure?.Invoke(services);

        return services.AddIpRateLimiting().BuildServiceProvider();
    }

    private sealed class SilentRefusal : IIpRateLimitRefusal
    {
        public Task WriteAsync(HttpContext http, string message) => Task.CompletedTask;
    }

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
