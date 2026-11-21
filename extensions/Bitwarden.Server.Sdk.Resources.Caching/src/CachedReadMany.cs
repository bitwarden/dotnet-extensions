using Bitwarden.Server.Sdk.Data;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// What a cached read-many declares beyond what every cached resource does.
/// </summary>
/// <param name="Party">The party whose rows it is: the key, always the party a request's context names.</param>
/// <param name="Scope">The scope its operation demands.</param>
/// <param name="AtMost">The most rows an entry holds; a party with more is answered by the database alone.</param>
internal sealed record CachedReadMany(Owner Party, string Scope, int AtMost);
