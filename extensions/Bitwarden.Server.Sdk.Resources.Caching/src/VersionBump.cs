using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A row whose version the database moves on, as the change commits: <c>SET version = version + 1</c> under the row's
/// lock, with the version it moved from as the dead one.
/// </summary>
/// <param name="Entity">The row's entity type: every cacheable query of its table has its entry invalidated, under the
/// keys read back from the row.</param>
/// <param name="Key">The row's key properties the update finds it by.</param>
/// <param name="Values">Their values, as the database holds them.</param>
/// <param name="MovedFrom">The entry keys the row held before the change, where it moved, each refused or not.</param>
/// <remarks>
/// For a version the change can't vouch for in the database: one that is not a concurrency token (an account's
/// <c>CacheVersion</c>, whose writes stay last-writer-wins), and the version of a row an included row's change moves
/// (<c>Including</c>). Two such changes to one row both read it at 6; moved on in the application, both would commit
/// 7 and mark 6 dead, and a reader could fill the first one's 7 after the second had invalidated. Moved on in the
/// database, the second waits for the first's lock and moves 7 to 8, marking 7 dead.
/// </remarks>
internal sealed record VersionBump(
    IEntityType Entity,
    IReadOnlyList<IProperty> Key,
    object?[] Values,
    IReadOnlyList<(string Key, bool Refuse)> MovedFrom)
{
    /// <summary>
    /// The row, identified by its type and key, for merging two bumps of it into one.
    /// </summary>
    public string Identity => Entity.Name + ":" + string.Join(",", Values.Select(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// Whether this tracked row is the one bumped.
    /// </summary>
    public bool Is(EntityEntry entry) =>
        entry.Metadata == Entity
        && Key.Select((property, i) => Equals(entry.Property(property.Name).CurrentValue, Values[i])).All(same => same);
}
