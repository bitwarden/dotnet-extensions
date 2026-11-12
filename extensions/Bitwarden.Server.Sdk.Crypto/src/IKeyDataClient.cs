namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Parses and validates the key service's <c>data</c>: a JSON string holding wrapped key material.
/// </summary>
/// <remarks>
/// Parsing is strict: unknown members are refused, and the <c>type</c> or <c>kind</c> member picks the shape. Every
/// crypto object in it is parsed with its SDK type, and ids two objects both name must be equal. Nothing is decrypted
/// or verified; comparing the returned ids with stored keys is the caller's job.
/// </remarks>
public interface IKeyDataClient
{
    /// <summary>
    /// Validates a key's <c>data</c>, of the type its <c>type</c> member names.
    /// </summary>
    /// <param name="data">The key's <c>data</c>.</param>
    /// <exception cref="InvalidKeyDataException">The data is refused.</exception>
    KeyInfo ValidateKey(string data);

    /// <summary>
    /// Validates an unlock method's <c>data</c>, of the kind its <c>kind</c> member names.
    /// </summary>
    /// <param name="data">The unlock method's <c>data</c>.</param>
    /// <exception cref="InvalidKeyDataException">The data is refused.</exception>
    UnlockMethodInfo ValidateUnlockMethod(string data);
}
