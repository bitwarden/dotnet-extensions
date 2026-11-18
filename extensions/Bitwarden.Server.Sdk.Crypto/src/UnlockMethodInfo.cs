namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// A validated unlock method's kind and the ids its <c>data</c> carries.
/// </summary>
/// <param name="Kind">The kind <c>data</c> named.</param>
/// <param name="KeyIds">The ids its objects carry.</param>
public sealed record UnlockMethodInfo(UnlockMethodKind Kind, KeyIds KeyIds);
