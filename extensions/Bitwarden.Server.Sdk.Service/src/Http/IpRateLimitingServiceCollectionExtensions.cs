using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Service.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers per-address request limits (<see cref="IpRateLimitOptions"/>).
/// </summary>
internal static class IpRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddIpRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<IpRateLimitOptions>().BindConfiguration(IpRateLimitOptions.SectionName);
        services.TryAddSingleton<IIpRateLimitRefusal, PlatformIpRateLimitRefusal>();
        services.AddOptions<DistributedIpRateLimitingOptions>().BindConfiguration(DistributedIpRateLimitingOptions.SectionName);
        services.TryAddSingleton(provider => new MemoryRateLimitCounters(provider.GetService<TimeProvider>() ?? TimeProvider.System));
        // Redis when distributed limiting is enabled and has a connection string, this process's
        // memory otherwise.
        services.TryAddSingleton<IRateLimitCounters>(provider =>
        {
            var distributed = provider.GetRequiredService<IOptions<DistributedIpRateLimitingOptions>>().Value;

            if (!distributed.Enabled || string.IsNullOrWhiteSpace(distributed.RedisConnectionString))
            {
                return provider.GetRequiredService<MemoryRateLimitCounters>();
            }

            return new RedisRateLimitCounters(
                RedisRateLimitCounters.Connect(distributed.RedisConnectionString),
                provider.GetService<IServiceIdentity>()?.Name ?? provider.GetRequiredService<IHostEnvironment>().ApplicationName,
                distributed,
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                provider.GetRequiredService<ILogger<RedisRateLimitCounters>>());
        });
        services.TryAddSingleton<IpRateLimiting>();

        return services;
    }
}
