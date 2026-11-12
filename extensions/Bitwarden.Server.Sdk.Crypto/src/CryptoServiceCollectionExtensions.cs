using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Extension methods for adding the Rust SDK backed crypto services.
/// </summary>
public static class CryptoServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICryptoParsingClient"/> and <see cref="IKeyDataClient"/> as one singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddServerSdkCrypto(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // One native client serves both interfaces.
        services.TryAddSingleton<NativeServerSdk>();
        services.TryAddSingleton<ICryptoParsingClient>(sp => sp.GetRequiredService<NativeServerSdk>());
        services.TryAddSingleton<IKeyDataClient>(sp => sp.GetRequiredService<NativeServerSdk>());

        return services;
    }
}
