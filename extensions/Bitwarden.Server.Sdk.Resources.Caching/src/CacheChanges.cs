using System.Data.Common;
using System.Globalization;
using Bitwarden.Server.Sdk.Errors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// What a change does to the cache: entries to tombstone, and rows whose version the database moves on (each another
/// entry to tombstone).
/// </summary>
internal sealed class CacheChanges
{
    public List<Invalidation> Invalidations { get; init; } = [];

    /// <summary>
    /// Rows whose version moves on in the database as the change commits (<see cref="VersionBump"/>).
    /// </summary>
    public List<VersionBump> Bumps { get; init; } = [];

    public bool IsEmpty => Invalidations.Count == 0 && Bumps.Count == 0;

    public void Add(CacheChanges other)
    {
        Invalidations.AddRange(other.Invalidations);
        Bumps.AddRange(other.Bumps);
    }

    public void Clear()
    {
        Invalidations.Clear();
        Bumps.Clear();
    }

    /// <summary>
    /// Carries them out, just before the change commits: moves the versions the database moves, then tombstones the
    /// entries.
    /// </summary>
    /// <param name="context">The context whose change it is.</param>
    /// <param name="transaction">The transaction about to commit; null where none is open and nothing is bumped.</param>
    /// <param name="store">The cache.</param>
    /// <param name="registry">The tombstones' lifetime.</param>
    /// <param name="times">How long an arrival's tombstone lives.</param>
    /// <param name="logger">Records a change committed though the cache couldn't be told.</param>
    /// <param name="cancellationToken">Cancels the bumps.</param>
    /// <exception cref="ServiceUnavailableException">
    /// The cache couldn't be told, and something the change makes stale is declared <see cref="CacheWrites.Refuse"/>:
    /// thrown before the commit, so the change isn't made.
    /// </exception>
    public async Task ApplyAsync(
        DbContext context,
        DbTransaction? transaction,
        IOwnedCacheStore store,
        OwnedCacheRegistry registry,
        OwnedCacheTimes times,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var invalidations = new List<Invalidation>(Invalidations);

        if (Bumps.Count > 0)
        {
            invalidations.AddRange(await BumpAsync(context, transaction, registry, cancellationToken));
        }

        // One tombstone per key: the newest version any of the changes makes dead, refused if any of them is, and an
        // arrival's shorter lifetime only where every change to the key is an arrival.
        var entries = invalidations
            .GroupBy(invalidation => invalidation.Key, StringComparer.Ordinal)
            .Select(key => new Invalidation(
                key.Key, key.Max(each => each.Dead), key.Any(each => each.Refuse), key.All(each => each.Arrival)))
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        try
        {
            await store.InvalidateAsync(entries, registry.TombstoneLifetime, times.CommitAllowance);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or ObjectDisposedException)
        {
            if (entries.Any(entry => entry.Refuse))
            {
                throw new ServiceUnavailableException("service cache", "could not be told of the change", exception);
            }

            logger.LogWarning(
                exception,
                "Could not invalidate {Count} service cache entries before a commit; every one is declared "
                + "to proceed (CacheWrites.Proceed), so the change commits and they stay stale until they expire.",
                entries.Count);
        }
    }

    /// <summary>
    /// Moves each bumped row's version on in the database, in the transaction about to commit, and reads back what it
    /// moved to and the row's keys: the version before it is the dead one, under every key the table's cacheable
    /// queries keep the row by.
    /// </summary>
    private async Task<List<Invalidation>> BumpAsync(
        DbContext context,
        DbTransaction? transaction,
        OwnedCacheRegistry registry,
        CancellationToken cancellationToken)
    {
        if (transaction?.Connection is not { } connection)
        {
            throw new InvalidOperationException(
                "A row whose version the database moves on was changed outside a transaction; the service cache can't be told.");
        }

        var sql = context.GetService<ISqlGenerationHelper>();
        var timeout = context.Database.GetCommandTimeout();
        var invalidations = new List<Invalidation>();

        foreach (var row in Bumps.GroupBy(bump => bump.Identity, StringComparer.Ordinal))
        {
            var bump = row.First();
            var entity = bump.Entity;
            var resources = registry.For(entity.ClrType);

            if (resources.Count == 0)
            {
                continue;
            }

            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var version = entity.FindProperty(resources[0].VersionProperty)!;
            var columns = resources.SelectMany(resource => resource.KeyColumns).Distinct(StringComparer.Ordinal).ToList();
            string Column(IProperty property) => sql.DelimitIdentifier(property.GetColumnName(table)!);

            var where = string.Join(
                " AND ",
                bump.Key.Select((property, i) => $"{Column(property)} = {sql.GenerateParameterName("p" + i.ToString(CultureInfo.InvariantCulture))}"));
            var name = sql.DelimitIdentifier(table.Name, table.Schema);

            await using (var update = Command(connection, transaction, timeout, bump, sql))
            {
                update.CommandText = $"UPDATE {name} SET {Column(version)} = {Column(version)} + 1 WHERE {where}";

                // Gone in this transaction (deleted with what included it): its own deletion tombstones it.
                if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    continue;
                }
            }

            long moved;
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);

            await using (var read = Command(connection, transaction, timeout, bump, sql))
            {
                read.CommandText = $"SELECT {string.Join(", ", columns.Select(column => Column(entity.FindProperty(column)!)).Prepend(Column(version)))} FROM {name} WHERE {where}";
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                await reader.ReadAsync(cancellationToken);
                moved = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);

                for (var i = 0; i < columns.Count; i++)
                {
                    values[columns[i]] = reader.IsDBNull(i + 1) ? null : reader.GetFieldValue<Guid>(i + 1);
                }
            }

            var keys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var resource in resources)
            {
                if (resource.EntryKey(registry.Service, column => values.GetValueOrDefault(column)) is { } key)
                {
                    keys.Add(key);
                    invalidations.Add(new Invalidation(key, moved - 1, resource.WhenUnreachable == CacheWrites.Refuse));
                }
            }

            foreach (var (from, refuse) in row.SelectMany(each => each.MovedFrom))
            {
                if (!keys.Contains(from))
                {
                    invalidations.Add(new Invalidation(from, moved - 1, refuse));
                }
            }

            Track(context, bump, version, moved);
        }

        return invalidations;
    }

    private static DbCommand Command(DbConnection connection, DbTransaction transaction, int? timeout, VersionBump bump, ISqlGenerationHelper sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (timeout is { } seconds)
        {
            command.CommandTimeout = seconds;
        }

        for (var i = 0; i < bump.Key.Count; i++)
        {
            command.Parameters.Add(bump.Key[i].GetRelationalTypeMapping().CreateParameter(
                command, sql.GenerateParameterName("p" + i.ToString(CultureInfo.InvariantCulture)), bump.Values[i], nullable: false));
        }

        return command;
    }

    /// <summary>
    /// The context's own copy of the row, if it tracks one, takes the version the database now holds, so a later save
    /// of it in the same context isn't refused for a version it moved itself.
    /// </summary>
    private static void Track(DbContext context, VersionBump bump, IProperty version, long moved)
    {
        // Mid-commit: look, without detecting changes, at what the context tracks.
        var detects = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry? entry;

        try
        {
            entry = context.ChangeTracker.Entries().FirstOrDefault(bump.Is);
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = detects;
        }

        if (entry is not { State: not EntityState.Deleted and not EntityState.Detached })
        {
            return;
        }

        var value = version.ClrType == typeof(long) ? (object)moved : checked((int)moved);
        var property = entry.Property(version.Name);

        // A row added in this transaction has no original values to set: its current ones become them as it is accepted.
        if (entry.State != EntityState.Added)
        {
            property.OriginalValue = value;
        }

        property.CurrentValue = value;

        if (entry.State == EntityState.Unchanged)
        {
            property.IsModified = false;
        }
    }
}
