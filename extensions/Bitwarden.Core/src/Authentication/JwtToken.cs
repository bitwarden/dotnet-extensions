using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;

namespace Bitwarden.Core;

public class JwtToken : IParsable<JwtToken>
{
    private static readonly JwtSecurityTokenHandler s_handler = new();

    private readonly string _token;
    private JwtSecurityToken? _decodedCache;

    internal JwtToken(string token)
    {
        _token = token;
    }

    public DateTime ValidTo
    {
        get
        {
            DecodeToken();
            return _decodedCache.ValidTo;
        }
    }

    public DateTime ValidFrom
    {
        get
        {
            DecodeToken();
            return _decodedCache.ValidFrom;
        }
    }

    [MemberNotNull(nameof(_decodedCache))]
    private void DecodeToken()
    {
        _decodedCache ??= s_handler.ReadJwtToken(_token);
    }

    public static JwtToken Parse(string s, IFormatProvider? provider)
    {
        // TODO: Add validation
        if (!s_handler.CanReadToken(s))
        {
            throw new FormatException("Not a valid JWT Token");
        }
        return new JwtToken(s);
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out JwtToken result)
    {
        result = null;

        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        try
        {
            result = Parse(s, provider);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public override string ToString()
        => _token;
}
