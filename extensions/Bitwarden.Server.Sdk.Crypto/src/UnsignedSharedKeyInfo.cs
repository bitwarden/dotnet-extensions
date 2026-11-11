namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// What an UnsignedSharedKey carries.
/// </summary>
/// <param name="Algorithm">The public-key algorithm it is encrypted with.</param>
public sealed record UnsignedSharedKeyInfo(UnsignedSharedKeyAlgorithm Algorithm);
