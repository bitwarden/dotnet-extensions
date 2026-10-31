using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Common;

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
/// address, is never kept as written. Its subject is the first sixteen bytes of a digest of the
/// answer's name, a line feed and the key as the owner compares it, read in order, so the subject's
/// hexadecimal is the digest's. The owner and its clients both reach the subject through
/// <see cref="Of(string, string, ReadOnlySpan{byte})"/>, from the same normalized text.
/// </para>
/// <para>
/// The digest is SHA-256 unless a secret is configured under <see cref="SecretKey"/>, and then it is
/// HMAC-SHA-256 keyed with that secret. A plain digest keeps the text out of the cache, but anyone who
/// can read the cache and guess an address can confirm it. A keyed digest can only be confirmed by
/// someone who also holds the secret. Every service and client sharing a cache must hold the same
/// secret: one with a different secret, or none, reaches subjects nobody fills, so it always asks
/// the owner.
/// </para>
/// </remarks>
public static class OwnedAnswerKey
{
    /// <summary>
    /// The OpenAPI extension that publishes the answers an operation gives.
    /// </summary>
    public const string Extension = "x-bitwarden-answer";

    /// <summary>
    /// The digest published for an answer keyed by text.
    /// </summary>
    public const string Digest = "sha-256";

    /// <summary>
    /// The configuration key of the secret that keys the digest: base64, at least
    /// <see cref="MinimumSecretLength"/> bytes. When unset the digest is plain SHA-256.
    /// </summary>
    public const string SecretKey = "Caching:Owned:AnswerSecret";

    /// <summary>
    /// The fewest bytes a configured secret may have.
    /// </summary>
    public const int MinimumSecretLength = 32;

    /// <summary>
    /// Reads the configured secret.
    /// </summary>
    /// <param name="configuration">The configuration holding <see cref="SecretKey"/>.</param>
    /// <returns>The secret, or empty when none is configured.</returns>
    /// <exception cref="InvalidOperationException">The secret is not base64, or is shorter than <see cref="MinimumSecretLength"/> bytes.</exception>
    public static byte[] ReadSecret(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[SecretKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        byte[] secret;
        try
        {
            secret = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{SecretKey} is not base64.", exception);
        }

        if (secret.Length < MinimumSecretLength)
        {
            throw new InvalidOperationException($"{SecretKey} must be at least {MinimumSecretLength} bytes.");
        }

        return secret;
    }

    /// <summary>
    /// The subject of a key written as text, by plain SHA-256.
    /// </summary>
    /// <param name="answer">The answer's name, for example <c>account-by-email</c>. It may not contain a line feed or <c>:</c>.</param>
    /// <param name="key">The key, normalized as the owner compares it (an address trimmed and lowercased).</param>
    public static Guid Of(string answer, string key) => Of(answer, key, []);

    /// <summary>
    /// The subject of a key written as text: by HMAC-SHA-256 with <paramref name="secret"/>, or by
    /// plain SHA-256 when it is empty.
    /// </summary>
    /// <param name="answer">The answer's name, for example <c>account-by-email</c>. It may not contain a line feed or <c>:</c>.</param>
    /// <param name="key">The key, normalized as the owner compares it (an address trimmed and lowercased).</param>
    /// <param name="secret">The configured secret (<see cref="ReadSecret"/>), or empty for none.</param>
    public static Guid Of(string answer, string key, ReadOnlySpan<byte> secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentNullException.ThrowIfNull(key);

        if (answer.AsSpan().IndexOfAny('\n', ':') >= 0)
        {
            throw new ArgumentException("An answer's name may not contain a line feed or ':'.", nameof(answer));
        }

        var text = Encoding.UTF8.GetBytes($"{answer}\n{key}");
        Span<byte> digest = stackalloc byte[32];
        if (secret.IsEmpty)
        {
            SHA256.HashData(text, digest);
        }
        else
        {
            HMACSHA256.HashData(secret, text, digest);
        }

        return new Guid(digest[..16], bigEndian: true);
    }

    /// <summary>
    /// The key template an answer's entries are stored under.
    /// </summary>
    /// <param name="service">The owning service's name.</param>
    /// <param name="answer">The answer's name.</param>
    public static string Template(string service, string answer) => $"{service}:answer:{answer}:{OwnedCacheEntry.KeyPlaceholder}";
}
