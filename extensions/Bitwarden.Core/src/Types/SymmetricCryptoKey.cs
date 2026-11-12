using System.Buffers.Text;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

using Bitwarden.Core.Json;

namespace Bitwarden.Core;

[JsonConverter(typeof(SymmetricCryptoKeyConverter))]
public class SymmetricCryptoKey : ISpanParsable<SymmetricCryptoKey>
{
    private static ReadOnlySpan<byte> NamePrefix => "bitwarden-"u8;

    const int KeyLength = 32;
    const int MacLength = 32;

    private readonly byte[] _rawData;

    public byte[] Key => _rawData[..KeyLength];
    public byte[]? Mac => _rawData.Length == KeyLength + MacLength ? _rawData[KeyLength..] : default;

    private SymmetricCryptoKey(ReadOnlySpan<byte> rawData)
    {
        _rawData = rawData.ToArray();
    }

    public static SymmetricCryptoKey Create(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> name, ReadOnlySpan<byte> info)
    {
        Debug.Assert(secret.Length == 16);

        Span<byte> keyBuffer = stackalloc byte[NamePrefix.Length + name.Length];
        NamePrefix.CopyTo(keyBuffer);
        name.CopyTo(keyBuffer[NamePrefix.Length..]);

        Span<byte> prk = stackalloc byte[HMACSHA256.HashSizeInBytes];
        var bytesWritten = HMACSHA256.HashData(keyBuffer, secret, prk);

        Debug.Assert(HMACSHA256.HashSizeInBytes == bytesWritten);

        Span<byte> destination = stackalloc byte[64];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, destination, info);
        return FromStretchedKey(destination);
    }

    internal static SymmetricCryptoKey FromStretchedKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyLength && key.Length != KeyLength + MacLength)
        {
            throw new FormatException("Invalid key length");
        }

        return new SymmetricCryptoKey(key);
    }

    public static SymmetricCryptoKey Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null)
    {
        Span<byte> buffer = stackalloc byte[64];
        Convert.TryFromBase64Chars(s, buffer, out _);
        return FromStretchedKey(buffer);
    }

    public static SymmetricCryptoKey Parse(ReadOnlySpan<byte> s, IFormatProvider? provider = null)
    {
        Span<byte> buffer = stackalloc byte[64];
        Base64.DecodeFromUtf8(s, buffer, out _, out _);
        return FromStretchedKey(buffer);
    }

    public static SymmetricCryptoKey Parse(string s, IFormatProvider? provider = null)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out SymmetricCryptoKey result)
    {
        return TryParse(s, out result);
    }

    public static bool TryParse(string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out SymmetricCryptoKey result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<char> s, [MaybeNullWhen(false)] out SymmetricCryptoKey result)
    {
        try
        {
            result = Parse(s);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public override string ToString()
    {
        return Convert.ToBase64String(_rawData);
    }
}
