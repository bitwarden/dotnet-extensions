namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// The public-key algorithm an UnsignedSharedKey is encrypted with.
/// </summary>
public enum UnsignedSharedKeyAlgorithm
{
    /// <summary>RSA-2048 OAEP with SHA-256.</summary>
    Rsa2048OaepSha256,

    /// <summary>RSA-2048 OAEP with SHA-1.</summary>
    Rsa2048OaepSha1,

    /// <summary>RSA-2048 OAEP with SHA-256, plus HMAC-SHA256.</summary>
    Rsa2048OaepSha256HmacSha256,

    /// <summary>RSA-2048 OAEP with SHA-1, plus HMAC-SHA256.</summary>
    Rsa2048OaepSha1HmacSha256,
}
