namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// The key ids a crypto object names, each as 32 lowercase hex characters.
/// </summary>
/// <param name="ContainedKeyId">The key this object holds, if it names one.</param>
/// <param name="EncryptedByKeyId">The key this object is encrypted or sealed with, if it names one.</param>
/// <param name="SignedByKeyId">The key that signed this object, if it is signed.</param>
public sealed record CryptoObjectKeyIds(string? ContainedKeyId, string? EncryptedByKeyId, string? SignedByKeyId);
