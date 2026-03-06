using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.Caching;

internal sealed class ConfigureFusionCacheOptions : IConfigureNamedOptions<FusionCacheOptions>
{
    public void Configure(string? name, FusionCacheOptions options)
    {
        if (name == null || name == Options.DefaultName)
        {
            return;
        }

        options.CacheName = name;
        options.CacheKeyPrefix = $"{name}:";
    }

    public void Configure(FusionCacheOptions options)
    {
        // Nothing to do here, the only reasonable thing would be to call Configure(Options.DefaultName, options)
        // would just return early.
    }
}
