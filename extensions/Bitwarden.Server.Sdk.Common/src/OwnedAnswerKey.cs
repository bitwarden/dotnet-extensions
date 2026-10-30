using System.Security.Cryptography;
using System.Text;

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
/// The subject is the key itself when it is an identifier. A key written as text (an address) is
/// never kept as written: its subject is the first sixteen bytes of the SHA-256 of the answer's name,
/// a line feed and the key as the owner compares it, so the text is never stored, and the same key
/// for two answers is two subjects. The owner and its clients both reach the subject through
/// <see cref="Of"/>, from the same normalized text.
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
    /// The subject of a key written as text.
    /// </summary>
    /// <param name="answer">The answer's name: for example <c>account-by-email</c>.</param>
    /// <param name="key">The key, normalized as the owner compares it (an address trimmed and lowercased).</param>
    public static Guid Of(string answer, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(answer);
        ArgumentNullException.ThrowIfNull(key);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{answer}\n{key}"));

        return new Guid(digest.AsSpan(0, 16));
    }

    /// <summary>
    /// The key template an answer's entries are stored under.
    /// </summary>
    /// <param name="service">The owning service's name.</param>
    /// <param name="answer">The answer's name.</param>
    public static string Template(string service, string answer) => $"{service}:answer:{answer}:{OwnedCacheEntry.KeyPlaceholder}";
}
