namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// The key ids a crypto object names, each as 32 lowercase hex characters.
/// </summary>
/// <remarks>
/// Read from headers only: nothing is decrypted or verified, so trust them no further than routing
/// and consistency checks.
/// </remarks>
/// <param name="ContainedKeyId">
/// The key this object holds (envelopes) or is (verifying keys), if it names one.
/// </param>
/// <param name="EncryptedByKeyId">The key this object is encrypted or sealed with, if it names one.</param>
/// <param name="SignedByKeyId">The key that signed this object, if it is signed.</param>
public sealed record CryptoObjectKeyIds(string? ContainedKeyId, string? EncryptedByKeyId, string? SignedByKeyId);
