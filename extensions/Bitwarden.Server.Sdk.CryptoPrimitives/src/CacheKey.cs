using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Bitwarden.Server.Sdk.CryptoPrimitives;

/// <summary>
/// A 32-byte key that derives cache keys, or a cache key derived from one.
/// </summary>
/// <remarks>
/// <see cref="DeriveCacheKey(CacheKey, ReadOnlySpan{string})"/> computes
/// <c>HMAC-SHA-256(secret, LP(p0) || ... || LP(pn))</c>, where
/// <c>LP(x) = uint64_be(len(utf8(x))) || utf8(x)</c>. Length-prefixing every parameter keeps
/// <c>("ab", "c")</c> and <c>("a", "bc")</c> apart.
/// </remarks>
public readonly struct CacheKey : IEquatable<CacheKey>
{
    /// <summary>
    /// The length of a cache key, in bytes.
    /// </summary>
    public const int Length = 32;

    private const int LengthPrefixSize = sizeof(ulong);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Bytes _bytes;

    /// <summary>
    /// Creates a cache key from exactly <see cref="Length"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not <see cref="Length"/> bytes.</exception>
    public CacheKey(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"A cache key is {Length} bytes, not {bytes.Length}.", nameof(bytes));
        }

        bytes.CopyTo(_bytes);
    }

    /// <summary>
    /// Creates a cache key from base64 that decodes to exactly <see cref="Length"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> is not base64 of <see cref="Length"/> bytes.</exception>
    public static CacheKey FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Decode into one spare byte, so a value longer than a key fails the length check.
        Span<byte> bytes = stackalloc byte[Length + 1];
        if (!Convert.TryFromBase64String(value, bytes, out var written) || written != Length)
        {
            throw new FormatException($"A cache key is base64 of {Length} bytes.");
        }

        return new CacheKey(bytes[..Length]);
    }

    /// <summary>
    /// A key used while in development mode with no key configured.
    /// </summary>
    public static CacheKey Development => default;

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
    public static CacheKey DeriveCacheKey(CacheKey secret, params ReadOnlySpan<string> parameters)
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

    // Holds the key inline, so CacheKey is a value with no heap allocation and default is all zeros.
    [InlineArray(Length)]
    private struct Bytes
    {
        private byte _element0;
    }
}
