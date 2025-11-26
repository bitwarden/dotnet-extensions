using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Caching;

internal class BitwardenCacheUseConfigureOptions : IConfigureNamedOptions<CacheOptions>
{
    private readonly IConfiguration _configuration;
    private readonly DefaultCacheOptions _defaultOptions;

    public BitwardenCacheUseConfigureOptions(IConfiguration configuration, IOptions<DefaultCacheOptions> defaultOptions)
    {
        _configuration = configuration;
        _defaultOptions = defaultOptions.Value;
    }

    public void Configure(string? name, CacheOptions options)
    {
        if (name == null || name == Options.DefaultName)
        {
            return;
        }

        options.Fusion.CacheName = name;
        options.Fusion.CacheKeyPrefix = $"{name}:";

        var section = _configuration.GetSection($"Caching:Uses:{name}");

        if (TryGetChild(section, nameof(CacheOptions.Redis), out var redisSection))
        {
            ApplyOverrideOrDefault(redisSection,
                nameof(CacheOptions.Redis.Configuration),
                (v) => options.Redis.Configuration = v,
                () => _defaultOptions.Redis.Configuration
            );
            ApplyOverrideOrDefault(redisSection,
                nameof(CacheOptions.Redis.InstanceName),
                (v) => options.Redis.InstanceName = v,
                ()=> _defaultOptions.Redis.InstanceName
            );
        }
        else
        {
            // No redis section at all, take all the defaults
            options.Redis.Configuration = _defaultOptions.Redis.Configuration;
            options.Redis.InstanceName = _defaultOptions.Redis.InstanceName;
        }

        if (TryGetChild(section, nameof(CacheOptions.Fusion), out var fusionSection))
        {
            // TODO: Decide what top level options can be defaulted

            if (TryGetChild(fusionSection, nameof(CacheOptions.Fusion.DefaultEntryOptions), out var entryOptionsSection))
            {
                ApplyOverrideOrDefault(entryOptionsSection,
                    nameof(CacheOptions.Fusion.DefaultEntryOptions.SkipDistributedCacheRead),
                    (v) => options.Fusion.DefaultEntryOptions.SkipDistributedCacheRead = v,
                    ()=> _defaultOptions.Fusion.DefaultEntryOptions.SkipDistributedCacheRead
                );

                ApplyOverrideOrDefault(entryOptionsSection,
                    nameof(CacheOptions.Fusion.DefaultEntryOptions.SkipDistributedCacheWrite),
                    (v) => options.Fusion.DefaultEntryOptions.SkipDistributedCacheWrite = v,
                    ()=> _defaultOptions.Fusion.DefaultEntryOptions.SkipDistributedCacheWrite
                );

                ApplyOverrideOrDefault(entryOptionsSection,
                    nameof(CacheOptions.Fusion.DefaultEntryOptions.Duration),
                    (v) => options.Fusion.DefaultEntryOptions.Duration = v,
                    ()=> _defaultOptions.Fusion.DefaultEntryOptions.Duration
                );
            }
        }
        else
        {
            options.Fusion = _defaultOptions.Fusion.GetInner();
            options.Fusion.CacheName = name;
            options.Fusion.CacheKeyPrefix = $"{name}:";
        }

        if (TryGetChild(section, nameof(CacheOptions.Memory), out var memorySection))
        {

        }
    }

    public void Configure(CacheOptions options)
    {
        // The only reasonable thing to do here would be Configure(Options.DefaultName, options)
        // but I know that will just quickly return so why bother
    }

    private static void ApplyOverrideOrDefault<T>(IConfigurationSection section, string key, Action<T?> setter, Func<T?> defaultGetter)
        where T : IParsable<T>
    {
        if (section.GetChildren().Any(c => c.Key == key))
        {
            // They have explicitly given a value
            setter(section[key] is { } value ? T.Parse(value, null) : default);
            return;
        }

        // Set item to default
        setter(defaultGetter());
    }

    private static bool TryGetChild(IConfigurationSection section, string childKey, [MaybeNullWhen(false)]out IConfigurationSection childSection)
    {
        childSection = section.GetChildren().FirstOrDefault(c => c.Key == childKey);
        if (childSection == null)
        {
            return false;
        }

        return true;
    }
}
