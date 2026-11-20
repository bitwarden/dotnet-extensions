using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Tells the cache of a context's changes as its transaction commits: after the change's statements have run, just
/// before the commit.
/// </summary>
/// <remarks>
/// <para>
/// Before the commit, so no reader can see the change while the cache still holds what it replaced: a fill of the
/// replaced version, however late, meets the tombstone. And so a change the cache can't be told of can still be
/// refused (<see cref="CacheWrites.Refuse"/>): the exception thrown here stops the commit, and the transaction rolls back.
/// </para>
/// <para>
/// A tombstone left by a transaction that then fails to commit, or rolls back, marks a version dead that is still the
/// row's: reads of it are misses until the tombstone expires, and nothing stale is served.
/// </para>
/// </remarks>
internal sealed class CacheTransactionInterceptor(
    OwnedCacheRegistry registry,
    IOwnedCacheStore store,
    OwnedCacheTimes times,
    ILogger<CacheInvalidationInterceptor> logger) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        Began(eventData);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        Began(eventData);
        return ValueTask.FromResult(result);
    }

    // A transaction another context began commits through that context's interceptor, which knows nothing of what this
    // one saved: its changes would commit with the cache never told. Refused, so no writer can bypass invalidation.
    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result) =>
        throw SharedTransaction();

    public override ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default) =>
        throw SharedTransaction();

    private static InvalidOperationException SharedTransaction() =>
        new("A context with a service cache can't join a transaction another context began (UseTransaction): the cache "
            + "would not be told of its changes as the transaction commits. Save through the context that began it.");

    public override InterceptionResult TransactionCommitting(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result)
    {
        CommittingAsync(transaction, eventData, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        await CommittingAsync(transaction, eventData, cancellationToken);
        return result;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Forget(eventData);

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Forget(eventData);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => Failed(eventData);

    public override Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Failed(eventData);
        return Task.CompletedTask;
    }

    private async Task CommittingAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        var pending = PendingInvalidations.Of(context);
        var changes = pending.TakeAll();

        if (changes.IsEmpty)
        {
            return;
        }

        try
        {
            await changes.ApplyAsync(context, transaction, store, registry, times, logger, cancellationToken);
        }
        catch
        {
            // The commit doesn't happen; if it is tried again, so is this.
            pending.Refused(changes);
            throw;
        }
    }

    private static void Began(TransactionEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            PendingInvalidations.Of(context).Began();
        }
    }

    private static void Failed(TransactionEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            PendingInvalidations.Of(context).Failed();
        }
    }

    private static void Forget(TransactionEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            PendingInvalidations.Of(context).Forget();
        }
    }
}
