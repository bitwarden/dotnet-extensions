namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// A validated key's type and the ids its <c>data</c> carries.
/// </summary>
/// <param name="DataType">The type <c>data</c> named.</param>
/// <param name="KeyIds">The ids its objects carry.</param>
public sealed record KeyInfo(KeyDataType DataType, KeyIds KeyIds);
