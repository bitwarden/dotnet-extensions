using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

using Bitwarden.Core.Json;

namespace Bitwarden.Core;

[JsonConverter(typeof(EncryptedStringConverter))]
public class EncryptedString : IDecryptable<SymmetricCryptoKey>
{
    public int Type { get; }

    // TODO: It should really be stored as bytes
    private readonly ReadOnlyMemory<char> _rest;
    internal EncryptedString(int type, ReadOnlySpan<char> rest)
    {
        Type = type;
        _rest = rest.ToArray();
    }

    public byte[] Decrypt(SymmetricCryptoKey key)
    {
        return Type switch
        {
            2 => DecryptAes256(_rest.Span, key),
            _ => throw new NotImplementedException(),
        };
    }

    private static byte[] DecryptAes256(ReadOnlySpan<char> encryptionPart, SymmetricCryptoKey symmetricCryptoKey)
    {
        var splitIndex = encryptionPart.IndexOf('|');
        if (splitIndex < 0)
        {
            throw new FormatException();
        }

        var ivPart = encryptionPart[..splitIndex];
        Span<byte> iv = stackalloc byte[16];
        var converted = Convert.TryFromBase64Chars(ivPart, iv, out var bytesWritten);
        if (!converted || bytesWritten != 16)
        {
            throw new FormatException();
        }

        encryptionPart = encryptionPart[++splitIndex..];
        splitIndex = encryptionPart.IndexOf('|');
        if (splitIndex < 0)
        {
            throw new FormatException();
        }

        var dataPart = encryptionPart[..splitIndex];

        var macPart = encryptionPart[++splitIndex..];
        Span<byte> mac = stackalloc byte[32];
        converted = Convert.TryFromBase64Chars(macPart, mac, out bytesWritten);
        if (!converted || bytesWritten != 32)
        {
            throw new FormatException();
        }

        // TODO: Validate mac

        Span<byte> data = stackalloc byte[dataPart.Length];
        converted = Convert.TryFromBase64Chars(dataPart, data, out bytesWritten);
        if (!converted)
        {
            throw new FormatException();
        }

        data = data[..bytesWritten];
        Span<byte> output = stackalloc byte[data.Length];

        using var aes = Aes.Create();
        aes.Key = symmetricCryptoKey.Key.ToArray();
        if (!aes.TryDecryptCbc(data, iv, output, out bytesWritten))
        {
            throw new Exception("Cannot decrypt");
        }

        output = output[..bytesWritten];

        return output.ToArray();
    }

    public static EncryptedString Parse(ReadOnlySpan<char> s)
    {
        // "2.uL3BnR/CukfhKIUfYoY2JA==|K8Wiqr5KG72B6cLxin3J3w==|vF5f2/wjHfmFuOrvhT3OAkrIK6xMqqKsPQoxy5ueQ8Y="
        var splitIndex = s.IndexOf('.');
        if (splitIndex < 0)
        {
            throw new FormatException();
        }

        var typePart = s[..splitIndex];
        var encryptionPart = s[++splitIndex..];

        if (!int.TryParse(typePart, out var type))
        {
            throw new FormatException();
        }
        
        return new EncryptedString(type, encryptionPart);
    }

    public static bool TryParse(ReadOnlySpan<char> s, [MaybeNullWhen(false)] out EncryptedString result)
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

    public static EncryptedString Encrypt(byte[] unencryptedData, SymmetricCryptoKey key)
    {
        Span<byte> iv = stackalloc byte[16];
        RandomNumberGenerator.Fill(iv);
        using var aes = Aes.Create();

        var maxOutputSize = (unencryptedData.Length + (aes.BlockSize / 8) - 1) / (aes.BlockSize / 8) * (aes.BlockSize / 8);
        Span<byte> destination = stackalloc byte[maxOutputSize];
        aes.Key = key.Key.ToArray();
        if (!aes.TryEncryptCbc(unencryptedData, iv, destination, out var bytesWritten))
        {
            throw new Exception("Cannot encrypt.");
        }

        // Trim output
        destination = destination[..bytesWritten];

        // TODO: validate mac

        // TODO: Make it so that we don't convert it to BASE64 just for this type
        var s = Convert.ToBase64String(destination);
        return new EncryptedString(2, s);
    }
}
