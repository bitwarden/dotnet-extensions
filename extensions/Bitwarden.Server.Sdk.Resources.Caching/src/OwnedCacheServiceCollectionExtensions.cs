using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Gives a service its owned cache: the resources its clients may read from Redis.
/// </summary>
public static class OwnedCacheServiceCollectionExtensions
{
    /// <summary>
    /// Gives this service the cache its clients read, for the resources its cacheable queries read.
    /// </summary>
    /// <typeparam name="TContext">The service's database.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>
    /// <para>
    /// The service is the only writer of its cache. Its keys are
    /// <c>{service name}:{resource type}:{identifier}</c>. Each registered
    /// <see cref="CacheableQuery{TEntity,TResource}"/> fills them as it reads, and a change to a row one is built from
    /// invalidates its entry just before the change commits, refusing the change when the cache can't be told unless
    /// its declaration says otherwise (<see cref="CacheWrites"/>).
    /// </para>
    /// <para>
    /// With no <c>Caching:Owned:Redis:Configuration</c> there is no cache: nothing is filled or
    /// invalidated, generated clients ask the service every time, and every read is the platform's
    /// read from the database. Never a memory-only cache: the owner's memory is invisible to its
    /// clients, so it would cache nothing they could use and invalidate nothing they hold.
    /// </para>
    /// <para>
    /// Outside <c>Development</c>, a host with a cache refuses to start when its connection names no Redis user of the
    /// service's own (<see cref="ServiceCacheAcl.RequireUser"/>).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.TryAddScoped&lt;IGetGroupQuery, GetGroupQuery&gt;();
    /// services.AddOwnedCache&lt;OrganizationDbContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddOwnedCache<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // The cacheable queries, found from the registrations as the container first needs them: this collection is the
        // one the container is built from, so a query registered after this call is found too.
        services.TryAddSingleton(_ => new CacheableQueries(services));
        services.TryAddScoped(provider => new CacheableQueryServices(provider, provider.GetRequiredService<TContext>()));
        services.TryAddSingleton<OwnedCacheRegistry>();

        // At startup, not on the first fill: a login that is Redis's default user fails to start. Outside Development,
        // entries only the owner may write need a user of the owner's own (ServiceCacheAcl).
        services.AddOptions<ServiceCacheUserCheck>()
            .Configure<IConfiguration, IHostEnvironment>((check, configuration, environment) =>
                check.OwnUser = ServiceCacheAcl.RequireUser(
                    configuration[OwnedCacheEntry.ConnectionKey], OwnedCacheEntry.ConnectionKey, environment.EnvironmentName))
            .ValidateOnStart();
        services.TryAddSingleton(provider =>
            new OwnedCacheConnection(provider.GetRequiredService<IConfiguration>()[OwnedCacheEntry.ConnectionKey]!));
        services.TryAddSingleton<IOwnedCacheStore>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>()[OwnedCacheEntry.ConnectionKey];
            if (string.IsNullOrWhiteSpace(configuration))
            {
                return new NullOwnedCacheStore();
            }

            var logger = provider.GetRequiredService<ILogger<RedisOwnedCacheStore>>();
            ServiceCacheAcl.WarnWithoutUser(configuration, logger);
            return new RedisOwnedCacheStore(provider.GetRequiredService<OwnedCacheConnection>(), logger);
        });

        services.TryAddSingleton(provider => OwnedCacheTimes.From(provider.GetRequiredService<IConfiguration>()));
        services.TryAddSingleton<CacheInvalidationInterceptor>();
        services.TryAddSingleton<CacheTransactionInterceptor>();
        services.ConfigureDbContext<TContext>((provider, options) => options.AddInterceptors(
            provider.GetRequiredService<CacheInvalidationInterceptor>(),
            provider.GetRequiredService<CacheTransactionInterceptor>(),
            CacheBulkWriteGuard.Instance));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OwnedCacheStartup<TContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IErasureObserver, CacheErasureObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Bitwarden.Server.Sdk.Resources.Endpoints.IStandardEndpointCheck, CacheContractCheck>());
        services.ConfigureAll<OpenApiOptions>(options => options.AddOperationTransformer<CacheContractTransformer>());

        return services;
    }
}
