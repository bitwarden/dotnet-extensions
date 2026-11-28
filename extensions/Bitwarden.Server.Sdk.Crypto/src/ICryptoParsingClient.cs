namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Parses SDK crypto objects and reads the key ids they name, without decrypting or verifying.
/// </summary>
public interface ICryptoParsingClient
{
    /// <summary>
    /// Inspects an EncString: its algorithm and encrypting key.
    /// </summary>
    /// <param name="encString">A serialized EncString.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid EncString.</exception>
    EncStringInfo InspectEncString(string encString);

    /// <summary>
    /// Inspects an UnsignedSharedKey: its algorithm.
    /// </summary>
    /// <param name="sharedKey">A serialized UnsignedSharedKey.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid UnsignedSharedKey.</exception>
    UnsignedSharedKeyInfo InspectUnsignedSharedKey(string sharedKey);

    /// <summary>
    /// Inspects a SymmetricKeyEnvelope: the sealed key and the sealing key.
    /// </summary>
    /// <param name="envelope">A base64 serialized SymmetricKeyEnvelope.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectSymmetricKeyEnvelope(string envelope);

    /// <summary>
    /// Inspects a LegacyCompatSymmetricKeyEnvelope: the sealed key and the sealing key.
    /// </summary>
    /// <param name="envelope">A serialized EncString or base64 SymmetricKeyEnvelope.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectLegacyCompatSymmetricKeyEnvelope(string envelope);

    /// <summary>
    /// Inspects a PasswordProtectedKeyEnvelope: the sealed key.
    /// </summary>
    /// <param name="envelope">A base64 serialized PasswordProtectedKeyEnvelope.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectPasswordProtectedKeyEnvelope(string envelope);

    /// <summary>
    /// Inspects a DataEnvelope: the content-encryption key it is sealed with.
    /// </summary>
    /// <param name="envelope">A base64 serialized DataEnvelope.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectDataEnvelope(string envelope);

    /// <summary>
    /// Inspects a SignedObject: the signing key.
    /// </summary>
    /// <param name="signedObject">A base64 COSE Sign1 object.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid SignedObject.</exception>
    CryptoObjectKeyIds InspectSignedObject(string signedObject);

    /// <summary>
    /// Inspects a VerifyingKey: its algorithm and key id.
    /// </summary>
    /// <param name="verifyingKey">A base64 COSE key.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid VerifyingKey.</exception>
    VerifyingKeyInfo InspectVerifyingKey(string verifyingKey);

    /// <summary>
    /// Inspects a PublicKey: its algorithm and key id.
    /// </summary>
    /// <param name="publicKey">A base64 DER SubjectPublicKeyInfo.</param>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid PublicKey.</exception>
    PublicKeyInfo InspectPublicKey(string publicKey);
}
