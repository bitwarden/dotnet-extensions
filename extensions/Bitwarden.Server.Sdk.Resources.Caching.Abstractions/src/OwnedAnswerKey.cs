using System.Text;
using Bitwarden.Server.Sdk.CryptoPrimitives;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// How a service-owned answer is keyed in the service cache, shared by the service that answers and
/// the clients that read it.
/// </summary>
/// <remarks>
/// <para>
/// An answer is one question an owner answers by one key, worked out from several of its tables,
/// for example whether this service holds the account at an address. It is kept in the same hash as
/// a cached resource (<see cref="OwnedCacheEntry"/>: <c>v</c>, and <c>d</c>, <c>none</c> or
/// <c>gone</c>), under <c>{service}:answer:{name}:{subject}</c>.
/// </para>
/// <para>
/// The subject is the key itself when it is an identifier. A key written as text, such as an
/// address, is never kept as written. Its subject is the first sixteen bytes of
/// <see cref="CacheKey.DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/> over the answer's name and
/// the key as the owner compares it, read in order, so the subject's hexadecimal is the derived key's.
/// The owner and its clients both reach the subject through
/// <see cref="Of(string, string, CacheSecret)"/>, from the same normalized text.
/// </para>
/// <para>
/// The secret is configured under <see cref="SecretKey"/>. Only holders of the secret can confirm a
/// guessed address is in the cache, so outside <c>Development</c> a host with a service cache requires
/// one (<see cref="ReadSecret(IConfiguration, string)"/>), and in <c>Development</c> it falls back to
/// <see cref="CacheSecret.Development"/>. Every service and client sharing a cache must hold the same
/// secret: one with a different secret reaches subjects nobody fills, so it always asks the owner.
/// </para>
/// </remarks>
public static class OwnedAnswerKey
{
    /// <summary>
    /// The OpenAPI extension that publishes the answers an operation gives.
    /// </summary>
    public const string Extension = "x-bitwarden-answer";

    /// <summary>
    /// The derivation published for an answer keyed by text:
    /// <see cref="CacheKey.DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/>.
    /// </summary>
    /// <remarks>
    /// The secret is a deployment's configuration, while the published contract is the same in every
    /// deployment, so every service and client sharing a cache learns of the secret from its own
    /// configuration.
    /// </remarks>
    public const string Digest = "cache-key";

    /// <summary>
    /// The configuration key of the secret that keys answers by text: base64 of
    /// <see cref="CacheSecret.Length"/> bytes.
    /// </summary>
    public const string SecretKey = "Caching:Owned:AnswerSecret";

    // Compared as IHostEnvironment.IsDevelopment compares it, without referencing hosting here.
    private const string DevelopmentEnvironment = "Development";

    /// <summary>
    /// Reads the configured secret, and refuses to go without one where it is required: a service
    /// cache is configured (<see cref="OwnedCacheEntry.ConnectionKey"/>) and the host is not in
    /// the <c>Development</c> environment.
    /// </summary>
    /// <remarks>
    /// A host calls this as it starts, so a deployment missing the secret fails to start rather than
    /// keep text-keyed answers under a key anyone can derive.
    /// </remarks>
    /// <param name="configuration">The configuration holding <see cref="SecretKey"/>.</param>
    /// <param name="environmentName">The host's environment name, for example <c>Production</c>.</param>
    /// <returns>
    /// The secret, or <see cref="CacheSecret.Development"/> when none is configured and none is required.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The secret is required and unset or is <see cref="CacheSecret.Development"/>, or is not base64 of
    /// <see cref="CacheSecret.Length"/> bytes.
    /// </exception>
    public static CacheSecret ReadSecret(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environmentName);

        var required = !string.IsNullOrWhiteSpace(configuration[OwnedCacheEntry.ConnectionKey])
            && !string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase);

        var value = configuration[SecretKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                throw new InvalidOperationException(
                    $"{SecretKey} must be set when a service cache is configured ({OwnedCacheEntry.ConnectionKey}) outside {DevelopmentEnvironment}.");
            }

            return CacheSecret.Development;
        }

        CacheSecret secret;
        try
        {
            secret = CacheSecret.FromString(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{SecretKey} is not base64 of {CacheSecret.Length} bytes.", exception);
        }

        // The development secret is public, so configuring it gives no more protection than none.
        if (required && secret == CacheSecret.Development)
        {
            throw new InvalidOperationException(
                $"{SecretKey} may not be the development secret when a service cache is configured ({OwnedCacheEntry.ConnectionKey}) outside {DevelopmentEnvironment}.");
        }

        return secret;
    }

    /// <summary>
    /// The subject of a key written as text: the first sixteen bytes of
    /// <see cref="CacheKey.DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/> over the answer's name
    /// and the key.
    /// </summary>
    /// <param name="answer">The answer's name, for example <c>account-by-email</c>. It may not contain <c>:</c>, <c>{</c> or <c>}</c>.</param>
    /// <param name="key">The key, normalized as the owner compares it (an address trimmed and lowercased).</param>
    /// <param name="secret">The configured secret (<see cref="ReadSecret(IConfiguration, string)"/>).</param>
    /// <exception cref="ArgumentException"><paramref name="answer"/> is empty or holds a character it may not, or the text is not valid UTF-16 (a lone surrogate).</exception>
    public static Guid Of(string answer, string key, CacheSecret secret)
    {
        OwnedCacheEntry.ThrowIfNotAName(answer);
        ArgumentNullException.ThrowIfNull(key);

        CacheKey derived;
        try
        {
            derived = CacheKey.DeriveCacheKey(secret, answer, key);
        }
        catch (EncoderFallbackException)
        {
            // Not chained: the inner exception's message quotes the character, and the key is text a
            // caller keeps out of the cache, such as an address.
            throw new ArgumentException("The answer's name or the key is not valid UTF-16 text.", nameof(key));
        }

        return new Guid(derived.AsSpan()[..16], bigEndian: true);
    }

    /// <summary>
    /// The key template an answer's entries are stored under.
    /// </summary>
    /// <param name="service">The owning service's name. It may not contain <c>:</c>, <c>{</c> or <c>}</c>.</param>
    /// <param name="answer">The answer's name. It may not contain <c>:</c>, <c>{</c> or <c>}</c>.</param>
    /// <exception cref="ArgumentException">A name is empty or holds a character it may not.</exception>
    public static string Template(string service, string answer)
    {
        OwnedCacheEntry.ThrowIfNotAName(answer);
        return $"{OwnedCacheEntry.Prefix(service)}{OwnedCacheEntry.AnswerResourceType}:{answer}:{OwnedCacheEntry.KeyPlaceholder}";
    }
}
