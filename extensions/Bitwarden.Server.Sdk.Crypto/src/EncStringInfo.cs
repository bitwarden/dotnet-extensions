namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// What an EncString carries.
/// </summary>
/// <param name="Algorithm">The algorithm it is encrypted with.</param>
/// <param name="EncryptedByKeyId">The id of the encrypting key, if it names one.</param>
public sealed record EncStringInfo(EncStringAlgorithm Algorithm, string? EncryptedByKeyId);
