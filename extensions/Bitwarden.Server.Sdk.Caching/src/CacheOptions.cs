using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.Caching;

/// <summary>
/// Options for customizing an individual usage of <see cref="IFusionCache"/>.
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// Configured fusion options used in <see cref="IFusionCache"/>.
    /// </summary>
    public FusionCacheOptions Fusion { get; set; } = new FusionCacheOptions();

    /// <summary>
    /// Configures the underlying <see cref="IMemoryCache"/> used in <see cref="IFusionCache"/>.
    /// </summary>
    public MemoryCacheOptions Memory { get; set; } = new MemoryCacheOptions();

    /// <summary>
    /// Configures Redis to be used for the <see cref="IDistributedCache"/> in <see cref="IFusionCache"/>.
    /// </summary>
    /// <remarks>
    /// Set <see cref="RedisCacheOptions.Configuration"/> to null to not use Redis
    /// </remarks>
    public RedisCacheOptions Redis { get; set; } = new RedisCacheOptions();
}
