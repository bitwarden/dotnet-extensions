namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// The key ids the objects in <c>data</c> carry, as 32 lowercase hex characters; null where no object names one.
/// </summary>
/// <param name="KeyId">The key pair's own id: its public or verifying key's.</param>
/// <param name="ContainedKeyId">The key an envelope holds.</param>
/// <param name="SealedByKeyId">The key that sealed it.</param>
/// <param name="SignedByKeyId">The key that signed it.</param>
public sealed record KeyIds(string? KeyId, string? ContainedKeyId, string? SealedByKeyId, string? SignedByKeyId);
