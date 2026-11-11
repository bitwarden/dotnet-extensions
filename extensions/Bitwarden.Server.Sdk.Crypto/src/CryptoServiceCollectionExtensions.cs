using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Extension methods for adding the Rust SDK backed crypto services.
/// </summary>
public static class CryptoServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICryptoParsingClient"/> as a singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServerSdkCrypto(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICryptoParsingClient, NativeServerSdk>();

        return services;
    }
}
