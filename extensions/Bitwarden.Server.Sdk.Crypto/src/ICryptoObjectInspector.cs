namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Strictly parses serialized SDK crypto objects and reports the key ids they name, without keys.
/// </summary>
/// <remarks>
/// Nothing is decrypted or verified. Key ids are 32 lowercase hex characters, e.g.
/// <c>5367fc06796da8e62b61afdc5c02b9f1</c>.
/// </remarks>
public interface ICryptoObjectInspector
{
    /// <summary>
    /// Inspects any EncString: symmetric (types 0, 2, 7) or asymmetric (types 3 to 6).
    /// </summary>
    /// <param name="encString">A serialized EncString, e.g. <c>7.g1g/owE6...</c>.</param>
    /// <returns>Its type, family and, for COSE EncStrings, the encrypting key id.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid EncString.</exception>
    EncStringInfo InspectEncString(string encString);

    /// <summary>
    /// Inspects an RSA-encrypted UnsignedSharedKey (EncString types 3 to 6).
    /// </summary>
    /// <param name="sharedKey">A serialized UnsignedSharedKey, e.g. <c>4.DMD1D5r6...</c>.</param>
    /// <returns>Its type and family. RSA EncStrings name no key.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid UnsignedSharedKey.</exception>
    EncStringInfo InspectUnsignedSharedKey(string sharedKey);

    /// <summary>
    /// Inspects a SymmetricKeyEnvelope.
    /// </summary>
    /// <param name="envelope">A base64 serialized SymmetricKeyEnvelope.</param>
    /// <returns>The sealed key id, if named, and the sealing key id.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectSymmetricKeyEnvelope(string envelope);

    /// <summary>
    /// Inspects a LegacyCompatSymmetricKeyEnvelope, in either its EncString or envelope form.
    /// </summary>
    /// <param name="envelope">A serialized EncString or base64 SymmetricKeyEnvelope.</param>
    /// <returns>
    /// The sealed and sealing key ids. The EncString form names no sealed key, and a sealing key only
    /// if it is COSE (type 7).
    /// </returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectLegacyCompatSymmetricKeyEnvelope(string envelope);

    /// <summary>
    /// Inspects a PasswordProtectedKeyEnvelope.
    /// </summary>
    /// <param name="envelope">A base64 serialized PasswordProtectedKeyEnvelope.</param>
    /// <returns>The sealed key id, if named. Legacy (non-COSE) sealed keys have none.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectPasswordProtectedKeyEnvelope(string envelope);

    /// <summary>
    /// Inspects a DataEnvelope.
    /// </summary>
    /// <param name="envelope">A base64 serialized DataEnvelope.</param>
    /// <returns>The id of the content-encryption key it is sealed with.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    CryptoObjectKeyIds InspectDataEnvelope(string envelope);

    /// <summary>
    /// Inspects a SignedObject.
    /// </summary>
    /// <param name="signedObject">A base64 COSE Sign1 object.</param>
    /// <returns>The signing key id.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid SignedObject.</exception>
    CryptoObjectKeyIds InspectSignedObject(string signedObject);

    /// <summary>
    /// Inspects a SignedPublicKey.
    /// </summary>
    /// <param name="signedPublicKey">A base64 serialized SignedPublicKey.</param>
    /// <returns>The signing key id.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid SignedPublicKey.</exception>
    CryptoObjectKeyIds InspectSignedPublicKey(string signedPublicKey);

    /// <summary>
    /// Inspects a VerifyingKey.
    /// </summary>
    /// <param name="verifyingKey">A base64 COSE key.</param>
    /// <returns>The key's own id, as <see cref="CryptoObjectKeyIds.ContainedKeyId"/>.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid VerifyingKey.</exception>
    CryptoObjectKeyIds InspectVerifyingKey(string verifyingKey);

    /// <summary>
    /// Validates an RSA PublicKey.
    /// </summary>
    /// <param name="publicKey">A base64 DER SubjectPublicKeyInfo.</param>
    /// <returns>No key ids: the SDK defines none for RSA public keys.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid PublicKey.</exception>
    CryptoObjectKeyIds InspectPublicKey(string publicKey);

    /// <summary>
    /// Inspects a SignedSecurityState. Its version is not returned: the SDK reads it only after
    /// verifying the signature.
    /// </summary>
    /// <param name="signedSecurityState">A base64 serialized SignedSecurityState.</param>
    /// <returns>The signing key id.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid SignedSecurityState.</exception>
    CryptoObjectKeyIds InspectSignedSecurityState(string signedSecurityState);
}
