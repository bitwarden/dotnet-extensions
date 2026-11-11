namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// What an EncString carries, read without decrypting it.
/// </summary>
/// <param name="EncryptionType">The numeric type prefix, e.g. <c>2</c> for <c>2.&lt;iv&gt;|&lt;data&gt;|&lt;mac&gt;</c>.</param>
/// <param name="Family">Symmetric or asymmetric, derived from <paramref name="EncryptionType"/>.</param>
/// <param name="EncryptedByKeyId">
/// The id of the encrypting key, as 32 lowercase hex characters. Only COSE EncStrings (type 7) carry one.
/// </param>
public sealed record EncStringInfo(int EncryptionType, EncStringFamily Family, string? EncryptedByKeyId);
