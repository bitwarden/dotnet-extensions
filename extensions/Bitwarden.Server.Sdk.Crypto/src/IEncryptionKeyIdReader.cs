namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Reads which key encrypted a piece of data, without decrypting it.
/// </summary>
/// <remarks>
/// Key ids are 32 lowercase hex characters, e.g. <c>5367fc06796da8e62b61afdc5c02b9f1</c>.
/// </remarks>
public interface IEncryptionKeyIdReader
{
    /// <summary>
    /// The id of the key <paramref name="encString"/> was encrypted with.
    /// </summary>
    /// <param name="encString">A serialized EncString, e.g. <c>7.g1g/owE6...</c>.</param>
    /// <returns>The key id, or <see langword="null"/> for legacy EncStrings, which carry none.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid EncString.</exception>
    string? FromEncString(string encString);

    /// <summary>
    /// The id of the key <paramref name="envelope"/> is sealed with.
    /// </summary>
    /// <param name="envelope">A base64 serialized SymmetricKeyEnvelope.</param>
    /// <returns>The key id.</returns>
    /// <exception cref="InvalidEncryptedDataException">The input is not a valid envelope.</exception>
    string? FromSymmetricKeyEnvelope(string envelope);
}
