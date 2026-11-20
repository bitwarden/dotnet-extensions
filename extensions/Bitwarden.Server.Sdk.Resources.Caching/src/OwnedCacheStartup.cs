using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Checks, as the host starts, that every tombstone outlives what it must refuse and that every included navigation can
/// be invalidated, and says what a change to each table the cache is built from does when the cache can't be told.
/// </summary>
/// <typeparam name="TContext">The service's database.</typeparam>
/// <param name="registry">The declared resources.</param>
/// <param name="store">Whether there is a cache.</param>
/// <param name="times">The fill limit and the commit allowance.</param>
/// <param name="scopes">A scope to read the database's command timeout from.</param>
/// <param name="logger">Where each table's setting is said.</param>
internal sealed class OwnedCacheStartup<TContext>(
    OwnedCacheRegistry registry,
    IOwnedCacheStore store,
    OwnedCacheTimes times,
    IServiceScopeFactory scopes,
    ILogger<OwnedCacheStartup<TContext>> logger) : IHostedService
    where TContext : DbContext
{
    /// <summary>
    /// The command timeout ADO.NET providers use when the context sets none.
    /// </summary>
    internal static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(30);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>();

        // Also reads what each resource's read includes, refusing an include the cache can't invalidate: with a cache or
        // without, as the guard against writes in bulk reads the same tables either way.
        var tables = registry.Tables(database);
        RequireOnlyRowOwnership(registry, database);

        if (!store.Enabled)
        {
            return Task.CompletedTask;
        }

        var timeout = database.Database.GetCommandTimeout() is { } seconds ? TimeSpan.FromSeconds(seconds) : DefaultCommandTimeout;
        Check(registry.TombstoneLifetime, times, timeout);

        foreach (var (table, writes) in tables.OrderBy(table => table.Key.Name, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "A change to {Table} the service cache can't be told of before it commits is {Writes}.",
                table.Name,
                writes == CacheWrites.Refuse ? "refused (503)" : "committed with a warning");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Refuses a cached resource whose row, or a row its read includes, carries a query filter other than the platform's
    /// row ownership: the filter is a rule of the read a client of the cache doesn't repeat, so a cached copy would
    /// answer a read the service wouldn't.
    /// </summary>
    /// <exception cref="InvalidOperationException">A filter of the service's own.</exception>
    internal static void RequireOnlyRowOwnership(OwnedCacheRegistry registry, DbContext database)
    {
        foreach (var resource in registry.Resources)
        {
            var tables = resource.Included(database).Select(navigation => navigation.TargetEntityType)
                .Prepend(database.Model.FindEntityType(resource.Entity)!);

            foreach (var table in tables)
            {
                if (Bitwarden.Server.Sdk.Data.RowLevelSecurity.OtherFilters(table) is [var other, ..])
                {
                    throw new InvalidOperationException(
                        $"{resource.Query.Name} caches {resource.Resource.Name}, but {table.ClrType.Name} has a query filter "
                        + $"of its own ({other}) besides row ownership. A client of the cache can't repeat it, so a cached "
                        + "copy would answer reads the service refuses. Read it with an ordinary query instead.");
                }
            }
        }
    }

    /// <summary>
    /// Refuses times under which a tombstone could expire before a fill it must refuse arrives, an arrival's among them,
    /// which lives only the commit allowance.
    /// </summary>
    /// <exception cref="InvalidOperationException">They don't hold.</exception>
    internal static void Check(TimeSpan tombstone, OwnedCacheTimes times, TimeSpan commandTimeout)
    {
        if (times.FillLimit < TimeSpan.Zero || times.CommitAllowance <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{OwnedCacheTimes.Section}:FillLimit can't be negative, and {OwnedCacheTimes.Section}:CommitAllowance must be positive.");
        }

        if (times.CommitAllowance <= commandTimeout + times.FillLimit)
        {
            throw new InvalidOperationException(
                $"{OwnedCacheTimes.Section}:CommitAllowance ({times.CommitAllowance}) must exceed the database's command timeout "
                + $"({commandTimeout}) plus {OwnedCacheTimes.Section}:FillLimit ({times.FillLimit}): an arrival's tombstone living "
                + "less could be gone before a fill read before its change committed arrives.");
        }

        if (tombstone <= times.FillLimit + times.CommitAllowance)
        {
            throw new InvalidOperationException(
                $"The service cache's tombstones live {tombstone}, the longest declared lifetime, which must exceed "
                + $"{OwnedCacheTimes.Section}:FillLimit ({times.FillLimit}) plus {OwnedCacheTimes.Section}:CommitAllowance "
                + $"({times.CommitAllowance}): a tombstone gone sooner lets a fill read before its change put the old row back.");
        }
    }
}
