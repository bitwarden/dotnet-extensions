namespace Bitwarden.Core;

public class RefreshableAuthenticationPayload : AuthenticationPayload
{
    public RefreshableAuthenticationPayload(JwtToken acessToken, TimeSpan expiresIn, string tokenType, string refreshToken)
        : base(acessToken, expiresIn, tokenType)
    {
        RefreshToken = refreshToken;
    }

    public string RefreshToken { get; }
}
