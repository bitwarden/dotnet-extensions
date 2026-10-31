using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Service.Tests.Http;

/// <summary>
/// Shared counting's settings: on once a connection string is given, and Redis set aside after 10 failures within
/// 120 seconds unless configured otherwise.
/// </summary>
public sealed class DistributedIpRateLimitingOptionsTests
{
    [Fact]
    public void Defaults_EnabledWithoutARedis_TenFailuresIn120Seconds()
    {
        var options = new DistributedIpRateLimitingOptions();

        Assert.True(options.Enabled);
        Assert.Null(options.RedisConnectionString);
        Assert.Equal(10, options.MaxRedisTimeoutsThreshold);
        Assert.Equal(120, options.SlidingWindowSeconds);
    }

    [Fact]
    public void TheSection_BindsAsConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["globalSettings:distributedIpRateLimiting:redisConnectionString"] = "redis.internal:6379",
                ["globalSettings:distributedIpRateLimiting:enabled"] = "false",
                ["globalSettings:distributedIpRateLimiting:maxRedisTimeoutsThreshold"] = "3",
                ["globalSettings:distributedIpRateLimiting:slidingWindowSeconds"] = "30",
            })
            .Build();

        var options = configuration.GetSection(DistributedIpRateLimitingOptions.SectionName).Get<DistributedIpRateLimitingOptions>()!;

        Assert.Equal("redis.internal:6379", options.RedisConnectionString);
        Assert.False(options.Enabled);
        Assert.Equal(3, options.MaxRedisTimeoutsThreshold);
        Assert.Equal(30, options.SlidingWindowSeconds);
    }
}
