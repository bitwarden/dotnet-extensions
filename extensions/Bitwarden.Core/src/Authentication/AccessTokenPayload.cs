namespace Bitwarden.Core;

public class AccessTokenPayload : AuthenticationPayload
{
    internal AccessTokenPayload(
        JwtToken jwtToken,
        TimeSpan expiresIn,
        string tokenType,
        EncryptedString encryptedPayload)
        : base(jwtToken, expiresIn, tokenType)
    {
        EncryptedPayload = encryptedPayload;
    }

    public EncryptedString EncryptedPayload { get; }
}
