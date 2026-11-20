namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Where the service writes its owned cache. Only the owning service has one.
/// </summary>
internal interface IOwnedCacheStore
{
    /// <summary>
    /// Whether there is a cache at all.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Reads an entry's fields, for the owner's own read.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <returns>The fields, or null when there is no entry or it could not be read.</returns>
    Task<IReadOnlyDictionary<string, string>?> ReadAsync(string key);

    /// <summary>
    /// Writes an entry, in the current format (<see cref="OwnedCacheEntry.CurrentFormat"/>), unless the key holds a tombstone marking this version dead, or an entry at a newer version, or
    /// a different one at this version.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="version">The row's version as read; <see cref="CacheFill.NothingRecorded"/> for "nothing recorded".</param>
    /// <param name="owner">The row's owner, as JSON, or null for a row nobody owns.</param>
    /// <param name="data">The resource object, or null for "nothing recorded".</param>
    /// <param name="lifetime">How long the entry lives.</param>
    /// <returns>Whether the entry now holds it: written, or already held exactly.</returns>
    Task<bool> FillAsync(string key, long version, string? owner, string? data, TimeSpan lifetime);

    /// <summary>
    /// Leaves a tombstone marking each entry's dead version dead, unless the key already marks it so or holds a newer
    /// version. A tombstone already there is never made to expire sooner than it would have.
    /// </summary>
    /// <param name="invalidations">The entries, each with its dead version.</param>
    /// <param name="lifetime">How long a tombstone lives.</param>
    /// <param name="arrivalLifetime">
    /// How long an arrival's tombstone lives (<see cref="Invalidation.Arrival"/>): the commit allowance.
    /// </param>
    /// <exception cref="Exception">Redis couldn't be reached; the caller decides what the change does (<see cref="CacheWrites"/>).</exception>
    Task InvalidateAsync(IReadOnlyCollection<Invalidation> invalidations, TimeSpan lifetime, TimeSpan arrivalLifetime);
}
