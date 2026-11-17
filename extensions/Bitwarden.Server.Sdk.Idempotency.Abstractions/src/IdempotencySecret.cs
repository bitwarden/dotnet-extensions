using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// The secret a service derives its operations' write keys and identifiers with
/// (<see cref="IdempotentOperation.Begin(string, IdempotencySecret)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The secret is configured under <see cref="SecretKey"/>, one per service. Only the service that derived a key ever
/// derives it again, when it runs the same operation again, so the secret need only be the same on every instance of
/// the service, and stay the same while work is in flight: an operation run again under another secret sends new keys,
/// and its callees do its writes again.
/// </para>
/// <para>
/// Outside <c>Development</c> a secret is required (<see cref="ReadSecret(IConfiguration, string)"/>), so a key or
/// identifier is never one anyone can compute from the operation's key. In <c>Development</c> it falls back to
/// <see cref="CacheSecret.Development"/>.
/// </para>
/// </remarks>
public sealed class IdempotencySecret
{
    /// <summary>
    /// The configuration key of the secret: base64 of <see cref="CacheSecret.Length"/> bytes.
    /// </summary>
    public const string SecretKey = "Idempotency:Secret";

    // Compared as IHostEnvironment.IsDevelopment compares it.
    private const string DevelopmentEnvironment = "Development";

    // Internal, so every secret an operation begins with went through ReadSecret's checks.
    internal IdempotencySecret(CacheSecret value)
    {
        Value = value;
    }

    /// <summary>
    /// The secret itself.
    /// </summary>
    public CacheSecret Value { get; }

    /// <summary>
    /// Reads the configured secret, and refuses to go without one outside the <c>Development</c> environment.
    /// </summary>
    /// <param name="configuration">The configuration holding <see cref="SecretKey"/>.</param>
    /// <param name="environmentName">The host's environment name, for example <c>Production</c>.</param>
    /// <returns>
    /// The secret, or <see cref="CacheSecret.Development"/> when none is configured in <c>Development</c>: the only way,
    /// besides <see cref="Microsoft.Extensions.DependencyInjection.IdempotencySecretServiceCollectionExtensions.AddIdempotencySecret"/>,
    /// to get one.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The secret is unset or is <see cref="CacheSecret.Development"/> outside <c>Development</c>, or is not base64 of
    /// <see cref="CacheSecret.Length"/> bytes. The message names the key, never the value.
    /// </exception>
    public static IdempotencySecret ReadSecret(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new IdempotencySecret(ReadValue(configuration[SecretKey], environmentName));
    }

    /// <summary>
    /// Reads <paramref name="value"/> as <see cref="ReadSecret(IConfiguration, string)"/> reads the configured one.
    /// </summary>
    internal static CacheSecret ReadValue(string? value, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(environmentName);

        var required = !string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new InvalidOperationException($"{SecretKey} must be set outside {DevelopmentEnvironment}.");
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
            throw new InvalidOperationException($"{SecretKey} is not base64 of {CacheSecret.Length} bytes.");
        }

        // The development secret is public, so configuring it gives no more protection than none.
        if (required && secret == CacheSecret.Development)
        {
            throw new InvalidOperationException(
                $"{SecretKey} may not be the development secret outside {DevelopmentEnvironment}.");
        }

        return secret;
    }
}
