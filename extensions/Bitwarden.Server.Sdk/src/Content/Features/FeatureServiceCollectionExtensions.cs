using Bitwarden.Server.Sdk.Features;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions for features on <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
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
