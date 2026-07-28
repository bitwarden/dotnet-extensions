using Bitwarden.Server.Sdk.Environment;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Bitwarden environment services.
/// </summary>
public static class EnvironmentServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="Bitwarden.Server.Sdk.Environment.IBitwardenEnvironment"/> and its dependencies.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddBitwardenEnvironment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.TryAddSingleton<IVersionInfoAccessor, VersionInfoAccessor>();
        services.TryAddSingleton<IBitwardenEnvironment, RuntimeBitwardenEnvironment>();

        return services;
    }
}
