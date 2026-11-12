namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// What a key's <c>data</c> was.
/// </summary>
public enum KeyDataType
{
    /// <summary><c>legacy-compat-private-key-envelope</c>: a public-key encryption pair.</summary>
    PrivateKey,

    /// <summary><c>legacy-compat-signing-key-envelope</c>: a signing pair.</summary>
    SigningKey,

    /// <summary><c>legacy-compat-symmetric-key-envelope</c>: a symmetric key sealed by another.</summary>
    SymmetricKey,

    /// <summary><c>unsigned-shared-key</c>: a symmetric key sealed to a public key.</summary>
    SharedKey,

    /// <summary><c>signed-security-state</c>: a security state signed by its owner's signing key.</summary>
    SecurityState,
}
