using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using static Bitwarden.Server.Sdk.Caching.ConfigHelpers;

namespace Bitwarden.Server.Sdk.Caching;

internal sealed class ConfigureFusionCacheOptions : IConfigureNamedOptions<FusionCacheOptions>
{
    private readonly IConfiguration _configuration;

    public ConfigureFusionCacheOptions(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(string? name, FusionCacheOptions options)
    {
        if (name == null || name == Options.DefaultName)
        {
            return;
        }

        options.CacheName = name;
        options.CacheKeyPrefix = $"{name}:";

        var useFusion = _configuration.GetSection($"Caching:Uses:{name}:Fusion");
        var defaultFusion = _configuration.GetSection("Caching:Fusion");

        // FusionCacheOptions-level settings
        if (TryGetDefinedValue(useFusion, defaultFusion, nameof(options.DistributedCacheCircuitBreakerDuration), out string? cbStr)
            && TimeSpan.TryParse(cbStr, out var cb))
            options.DistributedCacheCircuitBreakerDuration = cb;

        // DefaultEntryOptions settings
        var useEntry = useFusion.GetSection(nameof(options.DefaultEntryOptions));
        var defaultEntry = defaultFusion.GetSection(nameof(options.DefaultEntryOptions));
        var entry = options.DefaultEntryOptions;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.Duration), out string? durationStr)
            && TimeSpan.TryParse(durationStr, out var duration))
            entry.Duration = duration;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.IsFailSafeEnabled), out string? failSafeStr)
            && bool.TryParse(failSafeStr, out var isFailSafeEnabled))
            entry.IsFailSafeEnabled = isFailSafeEnabled;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.FailSafeMaxDuration), out string? fsmStr)
            && TimeSpan.TryParse(fsmStr, out var failSafeMax))
            entry.FailSafeMaxDuration = failSafeMax;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.FailSafeThrottleDuration), out string? fstStr)
            && TimeSpan.TryParse(fstStr, out var failSafeThrottle))
            entry.FailSafeThrottleDuration = failSafeThrottle;

        // null is intentional: explicitly setting EagerRefreshThreshold to null disables eager refresh
        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.EagerRefreshThreshold), out string? ertStr))
            entry.EagerRefreshThreshold = ertStr is null ? null
                : float.TryParse(ertStr, CultureInfo.InvariantCulture, out var ert) ? ert : entry.EagerRefreshThreshold;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.FactorySoftTimeout), out string? fsoStr)
            && TimeSpan.TryParse(fsoStr, out var factorySoft))
            entry.FactorySoftTimeout = factorySoft;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.FactoryHardTimeout), out string? fhoStr)
            && TimeSpan.TryParse(fhoStr, out var factoryHard))
            entry.FactoryHardTimeout = factoryHard;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.DistributedCacheSoftTimeout), out string? dsoStr)
            && TimeSpan.TryParse(dsoStr, out var distSoft))
            entry.DistributedCacheSoftTimeout = distSoft;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.DistributedCacheHardTimeout), out string? dhoStr)
            && TimeSpan.TryParse(dhoStr, out var distHard))
            entry.DistributedCacheHardTimeout = distHard;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.AllowBackgroundDistributedCacheOperations), out string? bgStr)
            && bool.TryParse(bgStr, out var bgOps))
            entry.AllowBackgroundDistributedCacheOperations = bgOps;

        if (TryGetDefinedValue(useEntry, defaultEntry, nameof(entry.JitterMaxDuration), out string? jitterStr)
            && TimeSpan.TryParse(jitterStr, out var jitter))
            entry.JitterMaxDuration = jitter;
    }

    public void Configure(FusionCacheOptions options)
    {
        // Nothing to do here, the only reasonable thing would be to call Configure(Options.DefaultName, options)
        // would just return early.
    }
}
