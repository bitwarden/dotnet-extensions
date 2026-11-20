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
    /// Registers <see cref="IdempotencySecret"/>, read from <see cref="IdempotencySecret.SecretKey"/>
    /// (<see cref="IdempotencySecret.ReadSecret(IConfiguration, string, string)"/>) as the host starts, for the service the
    /// host's <see cref="IHostEnvironment.ApplicationName"/> names.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>
    /// The host reads and checks the secret as it starts, so a service deployed without one, or with a malformed one,
    /// fails to start, naming the setting, rather than failing its first message or job. A service that runs no
    /// operations doesn't call this. A container with no <see cref="IHostEnvironment"/> is treated as not
    /// <c>Development</c>, and has no service name, so reading the secret throws; one with no
    /// <see cref="IConfiguration"/> is treated as having no secret. A secret registered before this call is kept.
    /// </remarks>
    public static IServiceCollection AddIdempotencySecret(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(provider =>
        {
            var environment = provider.GetService<IHostEnvironment>();
            var secret = IdempotencySecret.ReadValue(
                provider.GetService<IConfiguration>()?[IdempotencySecret.SecretKey],
                environment?.EnvironmentName ?? Environments.Production);

            return new IdempotencySecret(
                secret,
                environment?.ApplicationName
                    ?? throw new InvalidOperationException(
                        $"{nameof(AddIdempotencySecret)} needs the host's {nameof(IHostEnvironment)} for the service's name."));
        });
        // Made as the host starts, reading the secret, so a missing or malformed one stops the host.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IdempotencySecretCheck>(provider =>
        {
            provider.GetRequiredService<IdempotencySecret>();
            return new IdempotencySecretCheck();
        }));

        return services;
    }

    private sealed class IdempotencySecretCheck : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
