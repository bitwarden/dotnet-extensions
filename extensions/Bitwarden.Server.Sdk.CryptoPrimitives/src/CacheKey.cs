using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Bitwarden.Server.Sdk.CryptoPrimitives;

/// <summary>
/// A 32-byte cache key derived from a <see cref="CacheSecret"/> and a list of parameters.
/// </summary>
/// <remarks>
/// <see cref="DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/> computes
/// <c>HMAC-SHA-256(secret, LP(p0) || ... || LP(pn))</c>, where
/// <c>LP(x) = uint64_be(len(utf8(x))) || utf8(x)</c>. Length-prefixing every parameter keeps
/// <c>("ab", "c")</c> and <c>("a", "bc")</c> apart.
/// </remarks>
public readonly struct CacheKey : IEquatable<CacheKey>
{
    private const string Name = "cache key";

    /// <summary>
    /// The length of a cache key, in bytes.
    /// </summary>
    public const int Length = Bytes32.Length;

    private const int LengthPrefixSize = sizeof(ulong);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Bytes32 _bytes;

    /// <summary>
    /// Creates a cache key from exactly <see cref="Length"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not <see cref="Length"/> bytes.</exception>
    public CacheKey(ReadOnlySpan<byte> bytes)
    {
        _bytes = Bytes32.From(bytes, Name, nameof(bytes));
    }

    private CacheKey(Bytes32 bytes)
    {
        _bytes = bytes;
    }

    /// <summary>
    /// Creates a cache key from base64 that decodes to exactly <see cref="Length"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> is not base64 of <see cref="Length"/> bytes.</exception>
    public static CacheKey FromString(string value) => new(Bytes32.FromBase64(value, Name));

    /// <summary>
    /// Returns the key's bytes.
    /// </summary>
    [UnscopedRef]
    public ReadOnlySpan<byte> AsSpan() => _bytes;

    /// <summary>
    /// Returns a deterministic, non-colliding hash over the parameter list.
    /// </summary>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="EncoderFallbackException">A parameter is not valid UTF-16.</exception>
    public static CacheKey DeriveCacheKey(CacheSecret secret, params ReadOnlySpan<string> parameters)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, secret.AsSpan());
        Span<byte> prefix = stackalloc byte[LengthPrefixSize];

        // Feed LP(p) for each parameter, e.g. "ab" -> 00 00 00 00 00 00 00 02 61 62.
        foreach (var parameter in parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter, nameof(parameters));

            var encoded = StrictUtf8.GetBytes(parameter);
            BinaryPrimitives.WriteUInt64BigEndian(prefix, (ulong)encoded.Length);
            hmac.AppendData(prefix);
            hmac.AppendData(encoded);
        }

        Span<byte> digest = stackalloc byte[Length];
        hmac.GetHashAndReset(digest);

        return new CacheKey(digest);
    }

    /// <summary>
    /// Compares the keys in constant time.
    /// </summary>
    public bool Equals(CacheKey other) => CryptographicOperations.FixedTimeEquals(AsSpan(), other.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(AsSpan());

        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns the key as base64, which <see cref="FromString(string)"/> reads back.
    /// </summary>
    public override string ToString() => Convert.ToBase64String(AsSpan());

    /// <summary>
    /// Compares the keys in constant time.
    /// </summary>
    public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);

    /// <summary>
    /// Compares the keys in constant time.
    /// </summary>
    public static bool operator !=(CacheKey left, CacheKey right) => !left.Equals(right);
}
