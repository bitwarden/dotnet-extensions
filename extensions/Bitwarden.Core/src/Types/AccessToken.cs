using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Core;

public sealed class AccessToken : ISpanParsable<AccessToken>
{
    private static ReadOnlySpan<byte> Name => "accesstoken"u8;
    private static ReadOnlySpan<byte> Info => "sm-access-token"u8;


    private AccessToken(int version, Guid clientId, string clientSecret, SymmetricCryptoKey encryptionKey)
    {
        Version = version;
        ClientId = clientId;
        ClientSecret = clientSecret;
        EncryptionKey = encryptionKey;
    }

    public int Version { get; }
    public Guid ClientId { get; }
    public string ClientSecret { get; }
    public SymmetricCryptoKey EncryptionKey { get; }

    public static AccessToken Parse(string s, IFormatProvider? provider = null)
    {
        return Parse(s.AsSpan(), provider);
    }

    public static AccessToken Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null)
    {
        var splitIndex = s.IndexOf(':');
        if (splitIndex < 1)
        {
            throw new FormatException("Invalid format. (1)");
        }

        var firstPart = s[..splitIndex];
        var encryptionKeyPart = s[++splitIndex..];

        splitIndex = firstPart.IndexOf('.');

        if (splitIndex < 1)
        {
            throw new FormatException("Invalid format. (2)");
        }

        var versionPart = firstPart[..splitIndex];
        var rest = firstPart[++splitIndex..];

        if (!int.TryParse(versionPart, out var version) || version != 0)
        {
            throw new FormatException("Invalid format. (3)");
        }

        splitIndex = rest.IndexOf('.');

        if (splitIndex < 0)
        {
            throw new FormatException("Invalid format. (4)");
        }

        var serviceAccountIdPart = rest[..splitIndex];
        rest = rest[++splitIndex..];

        if (!Guid.TryParse(serviceAccountIdPart, out var serviceAccountId))
        {
            throw new FormatException("Invalid format. (5)");
        }

        var clientSecret = rest;

        // TODO: Do I know for sure this is always 16 bytes?
        Span<byte> destination = stackalloc byte[16];
        if (!Convert.TryFromBase64Chars(encryptionKeyPart, destination, out var bytesWritten))
        {
            throw new FormatException("Invalid format. (6)");
        }

        var encryptionKey = SymmetricCryptoKey.Create(destination, Name, Info);

        return new AccessToken(version, serviceAccountId, clientSecret.ToString(), encryptionKey);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out AccessToken result)
    {
        return TryParse(s.AsSpan(), provider, out result);
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out AccessToken result)
    {
        // TODO: Create real TryParse that doesn't need to catch exceptions for parse failures
        try
        {
            result = Parse(s, provider);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }
}
