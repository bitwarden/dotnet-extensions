namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// An entry a change makes stale: its key, and the version of it that the change makes dead.
/// </summary>
/// <param name="Key">The entry's key.</param>
/// <param name="Dead">
/// The version going out of date: a modified or deleted row's version before the change, or "nothing recorded"
/// (<see cref="CacheFill.NothingRecorded"/>) for a unique key a row has arrived at. The tombstone left behind refuses a
/// fill of it or of anything older, and accepts the version the change commits.
/// </param>
/// <param name="Refuse">Whether the change is refused when the cache can't be told (<see cref="CacheWrites.Refuse"/>).</param>
/// <param name="Arrival">
/// A row arriving under a key several rows may hold, marking every version dead: its tombstone lives the commit
/// allowance (<see cref="OwnedCacheTimes.CommitAllowance"/>), not the full tombstone lifetime. It need only refuse fills
/// read before the arrival committed; every read after the commit sees the arrival, and finds
/// either the one row or two, which it fills nothing for.
/// </param>
internal sealed record Invalidation(string Key, long Dead, bool Refuse = true, bool Arrival = false)
{
    /// <summary>
    /// Every version: for a key whose row is gone where nothing vouches for the version it went at (an erasure deletes
    /// in bulk, past every concurrency check). A fill of any version is refused until the tombstone expires.
    /// </summary>
    public const long EveryVersion = long.MaxValue / 2;
}
