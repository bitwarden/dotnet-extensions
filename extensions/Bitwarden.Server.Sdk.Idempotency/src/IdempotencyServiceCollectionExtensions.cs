using Bitwarden.Server.Sdk.Idempotency;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers idempotency keys' options and store.
/// </summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Adds idempotency keys: a write another service sends with an <c>Idempotency-Key</c> is done at most once, and
    /// its first answer replayed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Records are kept in Redis (<c>Idempotency:Redis:Configuration</c>) when it is set and the service's data
    /// protection key ring is shared, so every instance sees them; otherwise in this process. Settings are read from
    /// <c>Idempotency:*</c>.
    /// </para>
    /// <para>
    /// The middleware is added with <see cref="IdempotencyApplicationBuilderExtensions.UseIdempotencyKeys"/>.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The service's configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddIdempotencyKeys(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyOptions>().Bind(configuration.GetSection(IdempotencyOptions.Section));

        // Seals what is kept, with the key ring the host configures, if any.
        services.AddDataProtection();

        services.TryAddSingleton<IIdempotencyStore>(provider =>
        {
            var connection = provider.GetRequiredService<IConfiguration>()[IdempotencyOptions.RedisConfigurationKey];

            // Shared only when every instance can open what another sealed: a key ring persisted where they all read
            // it. Otherwise a record kept by one instance would be unopenable at the next,
            // so the record stays in the process that can open it.
            var sharedKeyRing = provider.GetService<IOptions<KeyManagementOptions>>()?.Value.XmlRepository is not null;

            if (string.IsNullOrWhiteSpace(connection) || !sharedKeyRing)
            {
                return new MemoryIdempotencyStore(provider.GetService<TimeProvider>() ?? TimeProvider.System);
            }

            return new RedisIdempotencyStore(
                RedisIdempotencyStore.Connect(connection),
                provider.GetRequiredService<ILogger<RedisIdempotencyStore>>());
        });

        return services;
    }
}
