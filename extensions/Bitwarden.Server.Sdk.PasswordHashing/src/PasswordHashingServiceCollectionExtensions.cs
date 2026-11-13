using Bitwarden.Server.Sdk.PasswordHashing;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering <see cref="IServerPasswordHasher"/>.
/// </summary>
public static class PasswordHashingServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IServerPasswordHasher"/>.
    /// </summary>
    /// <remarks>
    /// This method is safe to be called multiple times.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddServerPasswordHasher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IServerPasswordHasher, ServerPasswordHasher>();

        return services;
    }
}
