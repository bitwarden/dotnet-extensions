namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// What a PublicKey carries.
/// </summary>
/// <param name="Algorithm">The encryption algorithm.</param>
/// <param name="KeyId">The key id, as 32 lowercase hex characters.</param>
public sealed record PublicKeyInfo(PublicKeyAlgorithm Algorithm, string KeyId);
