using Bitwarden.Server.Sdk.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using LaunchDarkly.Sdk.Server.Interfaces;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions for features on <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static IServiceCollection AddFeatureFlagServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();
        services.AddHttpContextAccessor();

        services.Configure<FeatureFlagOptions>(configuration.GetSection("Features"));

        services.TryAddSingleton<LaunchDarklyClientProvider>();

        // This needs to be scoped so a "new" ILdClient can be given per request, this makes it possible to
        // have the ILdClient be rebuilt if configuration changes but for the most part this will return a cached
        // client from LaunchDarklyClientProvider, effectively being a singleton.
        services.TryAddScoped<ILdClient>(sp => sp.GetRequiredService<LaunchDarklyClientProvider>().Get());
        services.TryAddScoped<IFeatureService, LaunchDarklyFeatureService>();

        return services;
    }

    /// <summary>
    /// Adds known feature flags to the <see cref="FeatureFlagOptions"/>. This makes these flags
    /// show up in <see cref="IFeatureService.GetAll()"/>.
    /// </summary>
    /// <param name="services">The service collection to customize the options on.</param>
    /// <param name="knownFlags">The flags to add to the known flags list.</param>
    /// <returns>The <see cref="IServiceCollection"/> to chain additional calls.</returns>
    public static IServiceCollection AddKnownFeatureFlags(this IServiceCollection services, IEnumerable<string> knownFlags)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<FeatureFlagOptions>(options =>
        {
            foreach (var flag in knownFlags)
            {
                options.KnownFlags.Add(flag);
            }
        });

        return services;
    }

    /// <summary>
    /// Adds feature flags and their values to the <see cref="FeatureFlagOptions"/>.
    /// </summary>
    /// <param name="services">The service collection to customize the options on.</param>
    /// <param name="flagValues">The flags to add to the flag values dictionary.</param>
    /// <returns>The <see cref="IServiceCollection"/> to chain additional calls. </returns>
    public static IServiceCollection AddFeatureFlagValues(
        this IServiceCollection services,
        IEnumerable<KeyValuePair<string, string>> flagValues)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<FeatureFlagOptions>(options =>
        {
            foreach (var flag in flagValues)
            {
                options.FlagValues[flag.Key] = flag.Value;
            }
        });

        return services;
    }
}
