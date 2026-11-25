using Bitwarden.Server.Sdk.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Invalidates what an owner's erasure deletes in bulk, which the change tracker never sees.
/// </summary>
/// <remarks>
/// <para>
/// Reads the keys of the rows about to go and has the erasure's transaction tombstone them just before it commits, as
/// the interceptor has a saved change's (<see cref="CacheTransactionInterceptor"/>). An erasure run outside a
/// transaction commits each delete as it runs, so its tombstones are written here, before the delete.
/// </para>
/// <para>
/// Every version of an erased row is dead, not only the one read here: a delete in bulk checks no concurrency token, so
/// a change committed between this read and the delete would leave a version newer than the one read, which a reader
/// could have filled. The rows are gone, so nothing newer is ever filled under an identifier; under a unique key, a row
/// that arrives there later is a miss until the tombstone expires.
/// </para>
/// </remarks>
/// <param name="registry">The declared resources.</param>
/// <param name="store">Where entries are tombstoned.</param>
/// <param name="times">How long an arrival's tombstone lives.</param>
/// <param name="logger">Records an erasure that went ahead though the cache couldn't be told.</param>
internal sealed class CacheErasureObserver(
    OwnedCacheRegistry registry,
    IOwnedCacheStore store,
    OwnedCacheTimes times,
    ILogger<CacheErasureObserver> logger) : IErasureObserver
{
    public async Task ErasingAsync<T>(DbContext database, IQueryable<T> rows, CancellationToken cancellationToken)
        where T : class
    {
        if (!store.Enabled)
        {
            return;
        }

        var changes = new CacheChanges();

        foreach (var resource in registry.For(typeof(T)))
        {
            var refuse = resource.WhenUnreachable == CacheWrites.Refuse;
            var keys = await Keys(rows, resource, registry.Service, cancellationToken);
            changes.Invalidations.AddRange(keys.Select(key => new Invalidation(key, Invalidation.EveryVersion, refuse)));
        }

        // Rows a cached read includes: their parents' entries were built from them. A bulk delete moves no version, so
        // every version of each parent is dead, as an erased row's is.
        foreach (var included in registry.IncludedIn(database, typeof(T)))
        {
            var template = included.Parent.Template(registry.Service);
            var refuse = included.Parent.WhenUnreachable == CacheWrites.Refuse;
            var parents = await Subjects(rows, included.Navigation.ForeignKey.Properties[0].Name, cancellationToken);
            changes.Invalidations.AddRange(parents.Select(parent => new Invalidation(OwnedCacheEntry.Key(template, parent), Invalidation.EveryVersion, refuse)));
        }

        if (changes.IsEmpty)
        {
            return;
        }

        if (database.Database.CurrentTransaction is not null)
        {
            PendingInvalidations.Of(database).Record(changes);
            return;
        }

        // No transaction: the delete commits as it runs, so the cache is told now, before it does.
        await changes.ApplyAsync(database, null, store, registry, times, logger, cancellationToken);
    }

    /// <summary>
    /// The entry keys of the rows about to go, under one query's key: its key and, within its owner, the owner.
    /// </summary>
    private static async Task<List<string>> Keys<T>(
        IQueryable<T> rows,
        ICacheableResource resource,
        string service,
        CancellationToken cancellationToken)
        where T : class
    {
        var row = System.Linq.Expressions.Expression.Parameter(typeof(T), "row");
        System.Linq.Expressions.Expression Column(int i) => i < resource.KeyColumns.Count
            ? System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Property(row, resource.KeyColumns[i]), typeof(Guid?))
            : System.Linq.Expressions.Expression.Constant(null, typeof(Guid?));
        var projection = System.Linq.Expressions.Expression.Lambda<Func<T, ErasedKey>>(
            System.Linq.Expressions.Expression.MemberInit(
                System.Linq.Expressions.Expression.New(typeof(ErasedKey)),
                System.Linq.Expressions.Expression.Bind(typeof(ErasedKey).GetProperty(nameof(ErasedKey.Key))!, Column(0)),
                System.Linq.Expressions.Expression.Bind(typeof(ErasedKey).GetProperty(nameof(ErasedKey.Owner))!, Column(1))),
            row);

        var found = await rows.Select(projection).ToListAsync(cancellationToken);

        return [.. found
            .Select(each => resource.EntryKey(service, column =>
                column == resource.KeyColumns[0] ? each.Key : each.Owner))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// An erased row's key columns.
    /// </summary>
    private sealed class ErasedKey
    {
        public Guid? Key { get; set; }

        public Guid? Owner { get; set; }
    }

    /// <summary>
    /// The distinct values of one identifier column of the erased rows: the parents rows a cached read includes belong
    /// to.
    /// </summary>
    private static async Task<List<Guid>> Subjects<T>(IQueryable<T> rows, string column, CancellationToken cancellationToken)
        where T : class
    {
        var row = System.Linq.Expressions.Expression.Parameter(typeof(T), "row");
        var projection = System.Linq.Expressions.Expression.Lambda<Func<T, Guid?>>(
            System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Property(row, column), typeof(Guid?)),
            row);

        var found = await rows.Select(projection).Distinct().ToListAsync(cancellationToken);

        return [.. found.OfType<Guid>()];
    }
}
