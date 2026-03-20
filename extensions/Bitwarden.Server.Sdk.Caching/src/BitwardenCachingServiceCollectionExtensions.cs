using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Locking;
using ZiggyCreatures.Caching.Fusion.NullObjects;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Bitwarden.Server.Sdk.Caching;

/// <summary>
/// Extension methods for adding and configuring Bitwarden-style caching.
/// </summary>
public static class BitwardenCachingServiceCollectionExtensions
{
    /// <summary>
    /// Adds Bitwarden-style caching to the services container. This makes <see cref="IFusionCache"/> available when
    /// injected using keyed services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method registers <see cref="IFusionCache"/> and many supporting services/options to enable creation through
    /// keyed services. The underlying services, or even <see cref="IFusionCache"/> itself can be overriden to support
    /// your specific needs.
    /// </para>
    /// <para>
    /// The following list are all services that can have custom implementations per key by doing
    /// <c>services.TryAddKeyedSingleton&lt;ServiceType, MyServiceImplementation&gt;("MyUse")</c>. When this is done,
    /// MyServiceImplementation takes precedence over the defaults provided by this method. This allows you to customize
    /// just the parts that you need.
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description><see cref="IFusionCache"/> - The main cache interface, registered as a keyed singleton.
    /// Each instance is isolated by its string key (e.g., "Users", "Products").</description>
    /// </item>
    /// <item>
    /// <description><see cref="IDistributedCache"/> - Backed by Redis (if configured) by default or relies on the
    /// presence of a non-keyed <see cref="IDistributedCache"/> that has been registered.</description>
    /// </item>
    /// <item>
    /// <description><see cref="IMemoryCache"/> - Provides fast in-memory caching with configurable size limits and
    /// and expiration policies.</description>
    /// </item>
    /// <item>
    /// <description><see cref="IFusionCacheBackplane"/> - Enables cache invalidation notifications across distributed
    /// application instances. This will use Redis when it is configured and <see cref="NullBackplane"/> if not.</description>
    /// </item>
    /// <item>
    /// <description><see cref="IFusionCacheSerializer"/> - Enables serialization of values when stored in the configured
    /// distributed cache. By default will use <see cref="FusionCacheSystemTextJsonSerializer"/>.</description>
    /// </item>
    /// </list>
    /// <para>
    /// Many parts of caching can also be customized through the named options pattern. The following is a list of options
    /// instances that can be configured using code like this:
    /// <code lang="C#">
    /// services.Configure&lt;Options&gt;("MyUse", options =>
    /// {
    ///     // ...
    /// });
    /// </code>
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description><see cref="FusionCacheOptions"/> - Customizes <strong>MANY</strong> options for the created
    /// <see cref="IFusionCache"/>. It's best to read the docs on that type to see your options.</description>
    /// </item>
    /// <item>
    /// <description><see cref="RedisCacheOptions"/> - Customizes whether or not to use Redis for the distributed cache
    /// and backplane.</description>
    /// </item>
    /// <item>
    /// <description><see cref="MemoryCacheOptions"/> - Customizes various aspects of the used <see cref="IMemoryCache"/>.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown at runtime when retrieving a cache (or many of its supporting services) if:
    /// <list type="bullet">
    /// <item><description>The service key is not a string.</description></item>
    /// <item><description>The service key is null, empty, or whitespace-only.</description></item>
    /// <item><description><see cref="KeyedService.AnyKey"/> is used as the key </description></item>
    /// <item><description>Redis is not configured and no <see cref="IDistributedCache"/> is registered</description></item>
    /// </list>
    /// </exception>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for additional chaining.</returns>
    public static IServiceCollection AddBitwardenCaching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<FusionCacheOptions>, ConfigureFusionCacheOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<RedisCacheOptions>, ConfigureRedisCacheOptions>());

        services.TryAddKeyedSingleton(KeyedService.AnyKey, (sp, key) =>
        {
            var stringKey = GetStringKey(key);
            var redisCacheOptions = GetOptions<RedisCacheOptions>(sp, stringKey);

            // They have configured redis, use RedisCache
            if (redisCacheOptions.IsSetup())
            {
                return new RedisCache(redisCacheOptions);
            }

            return sp.GetService<IDistributedCache>()
                ?? throw new InvalidOperationException($"Redis was not configured for '{stringKey}' and no non-keyed IDistributedCache was registered.");
        });

        services.TryAddKeyedSingleton<IFusionCacheBackplane>(KeyedService.AnyKey, (sp, key) =>
        {
            var stringKey = GetStringKey(key);
            var redisCacheOptions = GetOptions<RedisCacheOptions>(sp, stringKey);

            if (redisCacheOptions.IsSetup())
            {
                return new RedisBackplane(
                    new RedisBackplaneOptions
                    {
                        Configuration = redisCacheOptions.Configuration,
                        ConfigurationOptions = redisCacheOptions.ConfigurationOptions,
                        ConnectionMultiplexerFactory = redisCacheOptions.ConnectionMultiplexerFactory,
                    },
                    new NamedLogger<RedisBackplane>(
                        sp.GetRequiredService<ILoggerFactory>(),
                        $"Bitwarden.Server.Sdk.Caching.RedisBackplane.{stringKey}"
                    )
                );
            }

            return new NullBackplane();
        });

        services.TryAddKeyedSingleton<IMemoryCache>(KeyedService.AnyKey, (sp, key) =>
        {
            var stringKey = GetStringKey(key);
            var memoryCacheOptions = GetOptions<MemoryCacheOptions>(sp, stringKey);

            return new MemoryCache(
                memoryCacheOptions,
                sp.GetRequiredService<ILoggerFactory>()
            );
        });

        services.TryAddKeyedSingleton<IFusionCache>(KeyedService.AnyKey, (sp, key) =>
        {
            var stringKey = GetStringKey(key);
            var fusionCacheOptions = GetOptions<FusionCacheOptions>(sp, stringKey);

            var cache = new FusionCache(
                fusionCacheOptions,
                sp.GetRequiredKeyedService<IMemoryCache>(key),
                new NamedLogger<FusionCache>(
                    sp.GetRequiredService<ILoggerFactory>(),
                    $"Bitwarden.Server.Sdk.Caching.{stringKey}"
                ),
                // This is optional, but if a use configures one then this will make use of it
                sp.GetKeyedService<IFusionCacheMemoryLocker>(key)
            );

            var serializer = sp.GetKeyedService<IFusionCacheSerializer>(key);
            if (serializer is null)
            {
                cache.SetupSerializer(new FusionCacheSystemTextJsonSerializer());
            }
            else
            {
                cache.SetupSerializer(serializer);
            }

            cache.SetupDistributedCache(sp.GetRequiredKeyedService<IDistributedCache>(key));

            var backplane = sp.GetKeyedService<IFusionCacheBackplane>(key);
            if (backplane is not null)
            {
                cache.SetupBackplane(backplane);
            }
            // TODO: else: Maybe log an error or even throw in cloud environments that there is a missing backplane?

            return cache;
        });

        return services;
    }

    private static string GetStringKey(object? key)
    {
        if (ReferenceEquals(KeyedService.AnyKey, key))
        {
            throw new InvalidOperationException("It is not valid to retrieve an instance of IFusionCache using AnyKey, you must provide a string to retrieve it.");
        }

        // TODO: We could get more lax here and if the key is not a string we just coerce it to a string with `ToString()`
        if (key is not string stringKey)
        {
            throw new InvalidOperationException($"Service key of type {key?.GetType().FullName ?? "null"} is not valid for injecting IFusionCache, only string is valid.");
        }

        // The key needs to be usable in configuration, disallow whitespace only strings
        if (string.IsNullOrWhiteSpace(stringKey))
        {
            throw new InvalidOperationException($"Key for IFusionCache instance cannot be a string with only whitespace.");
        }

        return stringKey;
    }

    private static T GetOptions<T>(IServiceProvider services, string name)
    {
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<T>>();

        return optionsMonitor.Get(name);
    }
}
