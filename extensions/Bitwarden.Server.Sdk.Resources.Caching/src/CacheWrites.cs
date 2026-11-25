namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// What a change to what a cache entry is built from does when the service cache can't be told of it before it
/// commits.
/// </summary>
/// <remarks>
/// A change invalidates its entries just before it commits. When Redis can't be reached then, the entries it makes
/// stale stay readable by other services until they expire: what the change took away is still granted by every
/// reader of the old copy, and what it gave is still refused.
/// </remarks>
public enum CacheWrites
{
    /// <summary>
    /// The change is refused: its transaction is not committed, and the request that made it is answered <c>503</c>
    /// (<c>ServiceUnavailableException</c>). The default: nothing a reader acts on is ever stale past its commit.
    /// </summary>
    Refuse,

    /// <summary>
    /// The change commits anyway, with a warning, and its entries stay stale until they expire. Only for what no
    /// reader grants or refuses anything by.
    /// </summary>
    Proceed,
}
