using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Reads a <see cref="CacheSecret"/> from a setting: the one rule every secret that derives keys with
/// <see cref="CacheKey.DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/> is read by.
/// </summary>
/// <remarks>
/// <para>
/// A secret is base64 of <see cref="CacheSecret.Length"/> bytes. Where it is required, it must be set and must not be
/// <see cref="CacheSecret.Development"/>, which is public, so a derived key is never one anyone can compute. Where it
/// isn't, an unset secret is <see cref="CacheSecret.Development"/>. A secret is required outside the
/// <c>Development</c> environment (<see cref="RequiredIn(string)"/>), unless its owner narrows that further.
/// </para>
/// <para>
/// A message names the setting, never its value, and wraps no exception that might quote it.
/// </para>
/// </remarks>
public static class CacheSecretSetting
{
    // Compared as IHostEnvironment.IsDevelopment compares it, without referencing hosting here.
    private const string DevelopmentEnvironment = "Development";

    /// <summary>
    /// Whether a secret is required in an environment: everywhere but <c>Development</c>.
    /// </summary>
    /// <param name="environmentName">The host's environment name, for example <c>Production</c>.</param>
    /// <returns>True outside <c>Development</c>.</returns>
    public static bool RequiredIn(string environmentName)
    {
        ArgumentNullException.ThrowIfNull(environmentName);

        return !string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the secret configured under <paramref name="key"/>, required outside <c>Development</c>.
    /// </summary>
    /// <param name="configuration">The configuration holding <paramref name="key"/>.</param>
    /// <param name="key">The setting, for example <c>Idempotency:Secret</c>.</param>
    /// <param name="environmentName">The host's environment name, for example <c>Production</c>.</param>
    /// <param name="requiredWhen">
    /// What else makes it required, named in a refusal, for example <c>when counts are shared in Redis</c>.
    /// </param>
    /// <returns>
    /// The secret, or <see cref="CacheSecret.Development"/> when none is configured in <c>Development</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The secret is unset or is <see cref="CacheSecret.Development"/> outside <c>Development</c>, or is not base64 of
    /// <see cref="CacheSecret.Length"/> bytes.
    /// </exception>
    public static CacheSecret Read(IConfiguration configuration, string key, string environmentName, string? requiredWhen = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return Read(configuration[key], key, RequiredIn(environmentName), requiredWhen);
    }

    /// <summary>
    /// Reads <paramref name="value"/>, the text configured under <paramref name="key"/>.
    /// </summary>
    /// <param name="value">The configured text, or null when unset.</param>
    /// <param name="key">The setting, named in a refusal.</param>
    /// <param name="required">
    /// Whether a secret is required: at least <see cref="RequiredIn(string)"/>, and narrowed by the caller.
    /// </param>
    /// <param name="requiredWhen">What else makes it required, named in a refusal.</param>
    /// <returns>The secret, or <see cref="CacheSecret.Development"/> when none is configured and none is required.</returns>
    /// <exception cref="InvalidOperationException">
    /// The secret is required and unset or is <see cref="CacheSecret.Development"/>, or is not base64 of
    /// <see cref="CacheSecret.Length"/> bytes.
    /// </exception>
    public static CacheSecret Read(string? value, string key, bool required, string? requiredWhen = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var when = string.IsNullOrWhiteSpace(requiredWhen) ? "" : $" {requiredWhen}";

        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new InvalidOperationException($"{key} must be set{when} outside {DevelopmentEnvironment}.");
            }

            return CacheSecret.Development;
        }

        CacheSecret secret;
        try
        {
            secret = CacheSecret.FromString(value);
        }
        catch (FormatException)
        {
            // Not chained: the inner exception's message may quote the text it could not read.
            throw new InvalidOperationException($"{key} is not base64 of {CacheSecret.Length} bytes.");
        }

        // The development secret is public, so configuring it gives no more protection than none.
        if (required && secret == CacheSecret.Development)
        {
            throw new InvalidOperationException(
                $"{key} may not be the development secret{when} outside {DevelopmentEnvironment}.");
        }

        return secret;
    }
}
