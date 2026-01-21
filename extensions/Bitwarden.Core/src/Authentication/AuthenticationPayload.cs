namespace Bitwarden.Core;

public class AuthenticationPayload
{
    internal AuthenticationPayload(JwtToken accessToken, TimeSpan expiresIn, string tokenType)
    {
        AccessToken = accessToken;
        ExpiresIn = expiresIn;
        TokenType = tokenType;
    }

    public JwtToken AccessToken { get; }
    public TimeSpan ExpiresIn { get; }
    public string TokenType { get; }
}
