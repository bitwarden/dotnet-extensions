using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Locking;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Bitwarden.Server.Sdk.Caching;

/// <summary>
/// Extension methods for adding and configuring Bitwarden-style caching.
/// </summary>
public static class BitwardenCachingServiceCollectionExtensions
{
    /// <summary>
    /// Adds Bitwarden-style caching the services container. This makes
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for additional chaining.</returns>
    public static IServiceCollection AddBitwardenCaching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<DefaultCacheOptions>()
            .BindConfiguration("Caching");

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<CacheOptions>, BitwardenCacheUseConfigureOptions>());

        services.TryAddKeyedSingleton<IDistributedCache>(KeyedService.AnyKey, (sp, key) =>
        {
            var stringKey = GetStringKey(key);
            var options = GetOptions(sp, stringKey);

            // They have configured redis, use RedisCache
            if (!string.IsNullOrEmpty(options.Redis.Configuration))
            {
                return new RedisCache(
                    Options.Create(options.Redis)
                );
            }

            // TODO: Use database
            throw new NotImplementedException("Database based IDistributedCache is not implemented.");
        });

        services.TryAddKeyedSingleton<IMemoryCache>(KeyedService.AnyKey, (sp, key) =>
        {
            var stringKey = GetStringKey(key);
            var options = GetOptions(sp, stringKey);

            return new MemoryCache(
                Options.Create(options.Memory),
                sp.GetRequiredService<ILoggerFactory>()
            );
        });

        services.TryAddKeyedSingleton<IFusionCache>(KeyedService.AnyKey, (sp, key) =>
        {
            var stringKey = GetStringKey(key);
            var options = GetOptions(sp, stringKey);

            // TODO: Is this the best way to create FusionCache? Do we need to pass in anything else?
            var cache = new FusionCache(
                options.Fusion,
                sp.GetRequiredKeyedService<IMemoryCache>(key),
                new WrappingLogger(sp.GetRequiredService<ILoggerFactory>(), stringKey),
                sp.GetKeyedService<IFusionCacheMemoryLocker>(key)
            );

            var serializer = sp.GetKeyedService<IFusionCacheSerializer>(key);
            if (serializer is null)
            {
                // TODO: Give some way so that consumers can setup their own serializer options that are AOT safe
                cache.SetupSerializer(new FusionCacheSystemTextJsonSerializer());
            }
            else
            {
                cache.SetupSerializer(serializer);
            }

            cache.SetupDistributedCache(sp.GetRequiredKeyedService<IDistributedCache>(key));

            // TODO: Backplane will be null for self-host instances not using Redis
            var backplane = sp.GetKeyedService<IFusionCacheBackplane>(key);
            if (backplane is not null)
            {
                cache.SetupBackplane(backplane);
            }
            // TODO: else: Maybe log a warning or even throw in certain environments?

            return cache;
        });

        return services;
    }

    /// <summary>
    /// Allows for programatic driven customization of a named cache use. This is an alternative to
    /// </summary>
    /// <param name="services"></param>
    /// <param name="name"></param>
    /// <param name="configureOptions"></param>
    /// <returns></returns>
    public static IServiceCollection CustomizeCaching(this IServiceCollection services, string name, Action<CacheOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<CacheOptions>(name)
            .Configure(configureOptions);

        return services;
    }

    private static string GetStringKey(object? key)
    {
        // TODO: We could get more lax here and if the key is not a string we just coerce it to a string with `ToString()`
        if (key is not string stringKey)
        {
            throw new InvalidOperationException($"Service key of type {key?.GetType().FullName ?? "null"} is not valid for injecting IFusionCache, only string is valid.");
        }

        return stringKey;
    }

    private static CacheOptions GetOptions(IServiceProvider services, string name)
    {
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<CacheOptions>>();

        return optionsMonitor.Get(name);
    }
}
