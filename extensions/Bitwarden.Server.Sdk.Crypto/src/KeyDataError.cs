namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Why key service <c>data</c> was refused.
/// </summary>
public enum KeyDataError
{
    /// <summary>
    /// Not valid JSON of a known type or kind, or an object in it isn't the SDK type its member calls for.
    /// </summary>
    InvalidData,

    /// <summary>Two objects name different keys where they must name the same one.</summary>
    KeyIdMismatch,

    /// <summary>
    /// A master password's KDF settings are below the SDK's minimums or outside Argon2's limits, or its salt is empty.
    /// </summary>
    InvalidKdf,
}
