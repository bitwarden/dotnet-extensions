using Bitwarden.Server.Sdk.CryptoPrimitives;
using Bitwarden.Server.Sdk.Resources.Caching;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.RateLimiting;

/// <summary>
/// The secret the shared counters in Redis derive their keys with, so the Redis holds no address anyone can recover.
/// </summary>
/// <remarks>
/// <para>
/// The secret is configured under <see cref="SecretKey"/>. Every instance of a service must hold the same one, or they
/// count apart; services may hold different ones. It is its own, never another secret a service derives keys with (such
/// as <c>Idempotency:Secret</c>), so a leak of one exposes neither the other's keys nor its holders.
/// </para>
/// <para>
/// Only counts kept in Redis need it. Outside <c>Development</c> they require it
/// (<see cref="ReadSecret(IConfiguration, string)"/>); in <c>Development</c> it falls back to
/// <see cref="CacheSecret.Development"/>.
/// </para>
/// </remarks>
public static class RateLimitSecret
{
    /// <summary>
    /// The configuration key of the secret: base64 of <see cref="CacheSecret.Length"/> bytes.
    /// </summary>
    public const string SecretKey = "RateLimiting:Secret";

    /// <summary>
    /// Reads the configured secret, and refuses to go without one outside the <c>Development</c> environment.
    /// </summary>
    /// <param name="configuration">The configuration holding <see cref="SecretKey"/>.</param>
    /// <param name="environmentName">The host's environment name, for example <c>Production</c>.</param>
    /// <returns>
    /// The secret, or <see cref="CacheSecret.Development"/> when none is configured in <c>Development</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The secret is unset or is <see cref="CacheSecret.Development"/> outside <c>Development</c>, or is not base64 of
    /// <see cref="CacheSecret.Length"/> bytes. The message names the key, never the value.
    /// </exception>
    public static CacheSecret ReadSecret(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return CacheSecretSetting.Read(configuration, SecretKey, environmentName, "when counts are shared in Redis");
    }
}
