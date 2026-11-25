using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Sees every change to what the cache holds as it is saved, and has the transaction that commits it invalidate it
/// just before it does (<see cref="CacheTransactionInterceptor"/>).
/// </summary>
/// <remarks>
/// <para>
/// A change is recorded as its save begins, with the version of each entry it makes dead: a modified or deleted row's
/// version before the change, or "nothing recorded" for a unique key a row arrives at. A modified row whose version
/// the code did not move has it moved here, so what commits is newer than what is dead. The version is the row's
/// concurrency token, so a second change holding the same version fails in the database, and two changes can't both
/// mark one version dead and commit different successors.
/// </para>
/// <para>
/// A save outside a transaction is made to open one, even for one statement, so every change commits through the
/// transaction interceptor. Before its commit the entries are tombstoned: a reader can't fill what the change replaces
/// once the change is visible, and when the cache can't be told the change can still be refused
/// (<see cref="CacheWrites"/>).
/// </para>
/// </remarks>
/// <param name="registry">The declared resources.</param>
/// <param name="store">Where entries are tombstoned.</param>
/// <param name="times">How long an arrival's tombstone lives.</param>
/// <param name="logger">Records a change committed though the cache couldn't be told.</param>
internal sealed class CacheInvalidationInterceptor(
    OwnedCacheRegistry registry,
    IOwnedCacheStore store,
    OwnedCacheTimes times,
    ILogger<CacheInvalidationInterceptor> logger) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SavedAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await SavedAsync(eventData.Context, cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Unstage(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Unstage(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => Unstage(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Unstage(eventData.Context);
        return Task.CompletedTask;
    }

    private void Record(DbContext? context)
    {
        if (context is null || !store.Enabled)
        {
            return;
        }

        // Saving runs this before Entity Framework detects changes, so detect them first: a
        // property changed on a tracked row is not yet a modified entry.
        if (context.ChangeTracker.AutoDetectChangesEnabled)
        {
            context.ChangeTracker.DetectChanges();
        }

        var changes = new CacheChanges();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            // Every cacheable query of the table, each under its own key: a membership by identifier and by its person.
            foreach (var resource in registry.For(entry.Metadata.ClrType))
            {
                Invalidations(resource, entry, registry.Service, changes);
            }

            // A row a cached read includes: its parent's entry is built from it, so the parent's version moves on.
            foreach (var included in registry.IncludedIn(context, entry.Metadata.ClrType))
            {
                Parents(included, entry, changes);
            }
        }

        if (changes.IsEmpty)
        {
            return;
        }

        var pending = PendingInvalidations.Of(context);
        pending.Stage(changes);
        pending.RequireTransaction(context);
    }

    /// <summary>
    /// What a change to a row of a cached resource makes dead, under one query's key.
    /// </summary>
    internal static void Invalidations(ICacheableResource resource, EntityEntry entry, string service, CacheChanges changes)
    {
        var refuse = resource.WhenUnreachable == CacheWrites.Refuse;
        var (current, original) = resource.EntryKeysOf(service, entry);

        // A cached read-many is the party's rows, not one row: no row's version speaks for it. Any change to a row of
        // its table, for the party the row named before and the one it names after, marks every version dead for the
        // commit allowance, as an arrival does: a fill read before the change committed arrives within it, and a read
        // after it sees the change.
        if (resource.ReadMany is not null)
        {
            foreach (var key in new[] { original, current }.OfType<string>().Distinct(StringComparer.Ordinal))
            {
                changes.Invalidations.Add(new Invalidation(key, Invalidation.EveryVersion, refuse, Arrival: true));
            }

            return;
        }

        var version = entry.Property(resource.VersionProperty!);

        // A row arriving under a unique key replaces "nothing recorded". Under a key several rows may hold, the entry
        // may be another row's, whose version says nothing of this one's: every version is dead until the tombstone
        // expires, and while two rows hold the key nothing is filled again. That tombstone lives only the commit
        // allowance: it must refuse a fill read before the arrival committed (another row's entry, or "nothing
        // recorded"), which arrives within the allowance. A read after the commit sees the
        // arrival: one row, filled at its own version, or two, for which nothing is filled.
        var dead = resource.NotUnique ? Invalidation.EveryVersion : CacheFill.NothingRecorded;
        Invalidation Arrived(string key) => new(key, dead, refuse, Arrival: resource.NotUnique);

        switch (entry.State)
        {
            // A new row read by identifier had no entry to replace: nothing is cached for an identifier that did not
            // exist.
            case EntityState.Added:
                if (!resource.ById && current is not null)
                {
                    changes.Invalidations.Add(Arrived(current));
                }

                return;

            // The version it went at, which the delete's concurrency check vouches for; where nothing checks it, every
            // version.
            case EntityState.Deleted:
                if (original is not null)
                {
                    changes.Invalidations.Add(new Invalidation(
                        original,
                        version.Metadata.IsConcurrencyToken ? Version(version.OriginalValue) : Invalidation.EveryVersion,
                        refuse));
                }

                return;
        }

        if (current is not null && current != original)
        {
            // The row moved to another key, which held "nothing recorded" until now, or another row's entry.
            changes.Invalidations.Add(Arrived(current));
        }

        if (!version.Metadata.IsConcurrencyToken)
        {
            // Nothing in the database checks this version, so two changes could both move it from the same value. The
            // database moves it instead, under the row's lock, as the change commits (VersionBump), and every query of
            // the table has its entry invalidated under the keys read back then.
            version.CurrentValue = version.OriginalValue;
            version.IsModified = false;

            var key = entry.Metadata.FindPrimaryKey()!.Properties;
            changes.Bumps.Add(new VersionBump(
                entry.Metadata,
                key,
                [.. key.Select(property => entry.Property(property.Name).OriginalValue)],
                original is not null && original != current ? [(original, refuse)] : []));
            return;
        }

        if (!version.IsModified)
        {
            version.CurrentValue = version.Metadata.ClrType == typeof(long)
                ? (object)((long)version.OriginalValue! + 1)
                : (int)version.OriginalValue! + 1;
        }

        if (original is not null)
        {
            changes.Invalidations.Add(new Invalidation(original, Version(version.OriginalValue), refuse));
        }
    }

    /// <summary>
    /// Moves on the version of each parent an included row belongs to, as the row stood before the change and after: in
    /// the database, as the change commits (<see cref="VersionBump"/>), so the parent's entry is invalidated with the
    /// version it replaced as the dead one, and concurrent changes to its rows each move it once.
    /// </summary>
    private static void Parents(OwnedCacheRegistry.Included included, EntityEntry entry, CacheChanges changes)
    {
        var key = included.Navigation.ForeignKey;
        var parents = new HashSet<Guid>();

        foreach (var original in new[] { false, true })
        {
            if ((original && entry.State == EntityState.Added) || (!original && entry.State == EntityState.Deleted))
            {
                continue;
            }

            var value = original
                ? entry.Property(key.Properties[0].Name).OriginalValue
                : entry.Property(key.Properties[0].Name).CurrentValue;

            if (value is Guid parent)
            {
                parents.Add(parent);
            }
        }

        foreach (var parent in parents)
        {
            changes.Bumps.Add(new VersionBump(key.PrincipalEntityType, key.PrincipalKey.Properties, [parent], []));
        }
    }

    private static long Version(object? value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// The save succeeded. Outside a transaction it committed in its own, which carried its changes out; inside one they
    /// wait for its commit. What is left with no transaction open was written by nothing that commits through one (a
    /// save with no statements to send): carried out now.
    /// </summary>
    private async Task SavedAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        var pending = PendingInvalidations.Of(context);
        pending.RestoreAutoTransaction(context);
        pending.Saved();

        if (context.Database.CurrentTransaction is not null)
        {
            return;
        }

        var left = pending.TakeAll();

        if (!left.IsEmpty)
        {
            await left.ApplyAsync(context, null, store, registry, times, logger, cancellationToken);
        }
    }

    private static void Unstage(DbContext? context)
    {
        if (context is not null)
        {
            var pending = PendingInvalidations.Of(context);
            pending.RestoreAutoTransaction(context);
            pending.Unstage();
        }
    }
}
