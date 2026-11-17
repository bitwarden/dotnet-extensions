using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the secret a service derives its operations' write keys and identifiers with.
/// </summary>
public static class IdempotencySecretServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IdempotencySecret"/>, read from <see cref="IdempotencySecret.SecretKey"/> when first
    /// resolved (<see cref="IdempotencySecret.ReadSecret(IConfiguration, string)"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>
    /// A host resolves it as it begins an operation, not as it starts, so a host that never begins one needs no secret.
    /// A container with no <see cref="IHostEnvironment"/> is treated as not <c>Development</c>, and one with no
    /// <see cref="IConfiguration"/> as having no secret. A secret registered before this call is kept.
    /// </remarks>
    public static IServiceCollection AddIdempotencySecret(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(provider => new IdempotencySecret(IdempotencySecret.ReadValue(
            provider.GetService<IConfiguration>()?[IdempotencySecret.SecretKey],
            provider.GetService<IHostEnvironment>()?.EnvironmentName ?? Environments.Production)));

        return services;
    }
}
