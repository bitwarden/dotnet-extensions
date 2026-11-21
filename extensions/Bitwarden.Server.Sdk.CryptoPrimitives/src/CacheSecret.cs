using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Bitwarden.Server.Sdk.CryptoPrimitives;

/// <summary>
/// A 32-byte secret that cache keys are derived from with
/// <see cref="CacheKey.DeriveCacheKey(CacheSecret, ReadOnlySpan{string})"/>.
/// </summary>
public readonly struct CacheSecret : IEquatable<CacheSecret>
{
    private const string Name = "cache secret";

    /// <summary>
    /// The length of a cache secret, in bytes.
    /// </summary>
    public const int Length = Bytes32.Length;

    private readonly Bytes32 _bytes;

    /// <summary>
    /// Creates a cache secret from exactly <see cref="Length"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not <see cref="Length"/> bytes.</exception>
    public CacheSecret(ReadOnlySpan<byte> bytes)
    {
        _bytes = Bytes32.From(bytes, Name, nameof(bytes));
    }

    private CacheSecret(Bytes32 bytes)
    {
        _bytes = bytes;
    }

    /// <summary>
    /// A secret used while in development mode with no secret configured.
    /// </summary>
    public static CacheSecret Development => default;

    /// <summary>
    /// Creates a cache secret from base64 that decodes to exactly <see cref="Length"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> is not base64 of <see cref="Length"/> bytes.</exception>
    public static CacheSecret FromString(string value) => new(Bytes32.FromBase64(value, Name));

    /// <summary>
    /// Returns the secret's bytes.
    /// </summary>
    [UnscopedRef]
    public ReadOnlySpan<byte> AsSpan() => _bytes;

    /// <summary>
    /// Compares the secrets in constant time.
    /// </summary>
    public bool Equals(CacheSecret other) => CryptographicOperations.FixedTimeEquals(AsSpan(), other.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CacheSecret other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(AsSpan());

        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns the secret as base64, which <see cref="FromString(string)"/> reads back.
    /// </summary>
    public override string ToString() => Convert.ToBase64String(AsSpan());

    /// <summary>
    /// Compares the secrets in constant time.
    /// </summary>
    public static bool operator ==(CacheSecret left, CacheSecret right) => left.Equals(right);

    /// <summary>
    /// Compares the secrets in constant time.
    /// </summary>
    public static bool operator !=(CacheSecret left, CacheSecret right) => !left.Equals(right);
}
