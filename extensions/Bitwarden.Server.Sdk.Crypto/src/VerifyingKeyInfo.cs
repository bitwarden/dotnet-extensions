namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// What a VerifyingKey carries.
/// </summary>
/// <param name="Algorithm">The signature algorithm.</param>
/// <param name="KeyId">The key id, as 32 lowercase hex characters.</param>
public sealed record VerifyingKeyInfo(VerifyingKeyAlgorithm Algorithm, string KeyId);
