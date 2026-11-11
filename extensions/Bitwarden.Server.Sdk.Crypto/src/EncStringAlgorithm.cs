namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// The symmetric algorithm an EncString is encrypted with.
/// </summary>
public enum EncStringAlgorithm
{
    /// <summary>AES-256-CBC.</summary>
    Aes256Cbc,

    /// <summary>AES-256-CBC with HMAC-SHA256.</summary>
    Aes256CbcHmac,

    /// <summary>AES-256-GCM.</summary>
    Aes256Gcm,

    /// <summary>XAES-256-GCM.</summary>
    XAes256Gcm,

    /// <summary>XChaCha20-Poly1305.</summary>
    XChaCha20Poly1305,
}
