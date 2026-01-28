using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extensions for features on <see cref="IServiceCollection"/>.
/// </summary>
public static class FeatureServiceCollectionExtensions
{
    /// <summary>
    /// Adds Feature flag related services to <see cref="IServiceCollection"/>. This makes <see cref="IFeatureService"/>
    /// available. This method does not need to be called manually if <c>UseBitwardenSdk</c> is used alongside the
    /// MSBuild property <c>BitIncludeFeatures</c> is set to <c>true</c> which is the default value.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> to chain additional calls.</returns>
    public static IServiceCollection AddFeatureFlagServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();
        services.AddHttpContextAccessor();

        services.AddOptions<FeatureFlagOptions>()
            .BindConfiguration("Features");

        services.TryAddSingleton<IContextBuilder, AnonymousContextBuilder>();

        services.TryAddSingleton<ILaunchDarklyClientProvider, LaunchDarklyClientProvider>();
        services.TryAddSingleton<IVersionInfoAccessor, VersionInfoAccessor>();

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

    /// <summary>
    /// Adds a <see cref="IContextBuilder"/> to the service collection.
    /// </summary>
    /// <typeparam name="T">You custom implementation of <see cref="IContextBuilder"/>.</typeparam>
    /// <param name="services">The service collection to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection" /> for additional chaining.</returns>
    public static IServiceCollection AddContextBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IContextBuilder
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IContextBuilder, T>();

        return services;
    }
}
