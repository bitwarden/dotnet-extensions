namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// The signature algorithm of a VerifyingKey.
/// </summary>
public enum VerifyingKeyAlgorithm
{
    /// <summary>Ed25519.</summary>
    Ed25519,

    /// <summary>ML-DSA-44.</summary>
    MlDsa44,
}
