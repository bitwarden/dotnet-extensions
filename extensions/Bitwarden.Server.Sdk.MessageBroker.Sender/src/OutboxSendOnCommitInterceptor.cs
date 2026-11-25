using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Wakes the outbox dispatcher once a save that wrote outbox messages has committed.
/// </summary>
/// <remarks>
/// <para>
/// Does nothing while <see cref="MessageOutboxOptions.SendOnCommit"/> is off.
/// </para>
/// <para>
/// Signals when the transaction holding the rows commits, or, for a save with no transaction of its
/// own, as soon as the save returns. A rolled-back transaction or a failed save signals nothing.
/// </para>
/// </remarks>
internal sealed class OutboxSendOnCommitInterceptor<TContext>(OutboxWake<TContext> wake, IOptions<MessageOutboxOptions> options)
    : ISaveChangesInterceptor, IDbTransactionInterceptor
    where TContext : DbContext, IMessageOutboxContext
{
    private static readonly object Marker = new();

    // Saved but not yet committed; weak, so a context disposed with a transaction open is forgotten.
    private readonly ConditionalWeakTable<DbContext, object> _pending = new();

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Remember(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Remember(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Failed(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        Committed(eventData.Context);

    public Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Committed(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Forget(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Remember(DbContext? context)
    {
        if (options.Value.SendOnCommit
            && context is TContext
            && context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.State == EntityState.Added))
        {
            _pending.AddOrUpdate(context, Marker);
        }
    }

    // A save's own transaction has signalled on commit; one without a transaction is visible now.
    private void Saved(DbContext? context)
    {
        if (context is not null && context.Database.CurrentTransaction is null && _pending.Remove(context))
        {
            wake.Signal();
        }
    }

    // Inside a caller's transaction an earlier save's rows may still commit, so they stay pending.
    private void Failed(DbContext? context)
    {
        if (context is not null && context.Database.CurrentTransaction is null)
        {
            _pending.Remove(context);
        }
    }

    private void Committed(DbContext? context)
    {
        if (context is not null && _pending.Remove(context))
        {
            wake.Signal();
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _pending.Remove(context);
        }
    }
}
