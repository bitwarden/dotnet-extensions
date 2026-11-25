namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// The page of a cached read-many a call asks for.
/// </summary>
/// <param name="Number">The page, from one.</param>
/// <param name="Size">How many it holds.</param>
internal sealed record CachedPage(int Number, int Size);
