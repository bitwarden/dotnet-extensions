using Microsoft.Extensions.Caching.StackExchangeRedis;
using TypeFest.Net;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.Caching;

/// <summary>
/// A core set of properties that can be configured as the default for caching. If a specific use
/// does not have their own special cased values, these will be used.
/// </summary>
[Pick<RedisCacheOptions>("InstanceName", "Configuration")]
[MapFrom<RedisCacheOptions>()]
public partial class DefaultRedisCacheOptions;

/// <summary>
/// A core set of options that can be configured as the default for all uses of <see cref="IFusionCache"/>.
/// Based on <see cref="FusionCacheOptions"/>.
/// </summary>
public class DefaultFusionCacheOptions
{
    private readonly FusionCacheOptions _inner;

    /// <summary>
    /// Creates a new instance of <see cref="DefaultFusionCacheOptions"/>.
    /// </summary>
    public DefaultFusionCacheOptions()
    {
        _inner = new();
        DefaultEntryOptions = new DefaultFusionCacheEntryOptions(_inner);
    }

    /// <inheritdoc cref="FusionCacheOptions.DefaultEntryOptions"/>
    public DefaultFusionCacheEntryOptions DefaultEntryOptions { get; set; }

    internal FusionCacheOptions GetInner() => _inner.Duplicate();
}

/// <summary>
/// A core set of options that can be configured as the default for all uses of <see cref="IFusionCache"/>.
/// Based on <see cref="FusionCacheEntryOptions"/>.
/// </summary>
public class DefaultFusionCacheEntryOptions
{
    private readonly FusionCacheOptions _inner;

    internal DefaultFusionCacheEntryOptions(FusionCacheOptions inner)
    {
        _inner = inner;
    }

    /// <inheritdoc cref="FusionCacheEntryOptions.SkipDistributedCacheRead"/>
    public bool SkipDistributedCacheRead
    {
        get => _inner.DefaultEntryOptions.SkipDistributedCacheRead;
        set => _inner.DefaultEntryOptions.SkipDistributedCacheRead = value;
    }

    /// <inheritdoc cref="FusionCacheEntryOptions.SkipDistributedCacheWrite"/>
    public bool SkipDistributedCacheWrite
    {
        get => _inner.DefaultEntryOptions.SkipDistributedCacheWrite;
        set => _inner.DefaultEntryOptions.SkipDistributedCacheWrite = value;
    }

    /// <inheritdoc cref="FusionCacheEntryOptions.Duration"/>
    public TimeSpan Duration
    {
        get => _inner.DefaultEntryOptions.Duration;
        set => _inner.DefaultEntryOptions.Duration = value;
    }
}

/// <summary>
/// A core set of options that can be configured as the default for all uses of Caching.
/// </summary>
public class DefaultCacheOptions
{
    /// <summary>
    /// Redis options that can be configured as the default for all uses of Redis
    /// </summary>
    public DefaultRedisCacheOptions Redis { get; set; } = new DefaultRedisCacheOptions();

    /// <summary>
    /// Fusion options that can be configured as the default for all <see cref="IFusionCache"/> uses.
    /// </summary>
    public DefaultFusionCacheOptions Fusion { get; set; } = new DefaultFusionCacheOptions();
}
