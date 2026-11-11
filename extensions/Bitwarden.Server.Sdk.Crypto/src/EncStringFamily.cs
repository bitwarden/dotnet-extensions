namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Whether an EncString is encrypted with a symmetric key or an RSA public key.
/// </summary>
public enum EncStringFamily
{
    /// <summary>
    /// Types 0, 2 and 7.
    /// </summary>
    Symmetric,

    /// <summary>
    /// Types 3 to 6, i.e. UnsignedSharedKeys.
    /// </summary>
    Asymmetric,
}
