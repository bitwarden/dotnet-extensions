using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// What one context has changed that the cache must be told of before it commits.
/// </summary>
/// <remarks>
/// Two stages. What a save is about to write is <em>staged</em>; once the save succeeds inside an explicit transaction it
/// is <em>saved</em>, and waits for that transaction's commit. A save outside one commits inside itself, in the
/// transaction the platform makes it open, so its staged changes are carried out there. A save that fails drops only
/// what it staged; a transaction that rolls back, or ends without committing, drops everything.
/// </remarks>
internal sealed class PendingInvalidations
{
    private static readonly ConditionalWeakTable<DbContext, PendingInvalidations> Table = new();

    private readonly CacheChanges _staged = new();
    private readonly CacheChanges _saved = new();
    private readonly CacheChanges _refused = new();
    private AutoTransactionBehavior? _autoTransaction;

    public static PendingInvalidations Of(DbContext context) => Table.GetOrCreateValue(context);

    /// <summary>
    /// Notes what the save in progress will write.
    /// </summary>
    public void Stage(CacheChanges changes)
    {
        lock (_saved)
        {
            _staged.Add(changes);
        }
    }

    /// <summary>
    /// Notes what was written in the open transaction without a save (a bulk change), for its commit.
    /// </summary>
    public void Record(CacheChanges changes)
    {
        lock (_saved)
        {
            _saved.Add(changes);
        }
    }

    /// <summary>
    /// The save succeeded: what it staged and didn't commit waits for the transaction's commit.
    /// </summary>
    public void Saved()
    {
        lock (_saved)
        {
            _saved.Add(_staged);
            _staged.Clear();
        }
    }

    /// <summary>
    /// The save failed: what it staged was never written.
    /// </summary>
    public void Unstage()
    {
        lock (_saved)
        {
            _staged.Clear();
        }
    }

    /// <summary>
    /// A transaction is committing: everything to carry out before it does.
    /// </summary>
    public CacheChanges TakeAll()
    {
        lock (_saved)
        {
            var taken = new CacheChanges();
            taken.Add(_refused);
            taken.Add(_saved);
            taken.Add(_staged);
            _refused.Clear();
            _saved.Clear();
            _staged.Clear();
            return taken;
        }
    }

    /// <summary>
    /// The cache couldn't be told of what a commit was about to make, so the commit was stopped. Kept until the
    /// transaction ends: a second attempt to commit it must tell the cache too, and is stopped again if it still can't.
    /// </summary>
    public void Refused(CacheChanges changes)
    {
        lock (_saved)
        {
            _refused.Add(changes);
        }
    }

    /// <summary>
    /// The transaction rolled back: nothing it wrote happened.
    /// </summary>
    public void Forget()
    {
        lock (_saved)
        {
            _staged.Clear();
            _saved.Clear();
            _refused.Clear();
        }
    }

    /// <summary>
    /// Using the transaction failed (its commit, say): what it saved is dropped, but not what a refused commit left
    /// (<see cref="Refused"/>), which the transaction, still open, may try to commit again.
    /// </summary>
    public void Failed()
    {
        lock (_saved)
        {
            _staged.Clear();
            _saved.Clear();
        }
    }

    /// <summary>
    /// A transaction began: what an earlier one saved and never committed (it was disposed unfinished) is dropped. What
    /// the save beginning it staged is kept, for its commit.
    /// </summary>
    public void Began()
    {
        lock (_saved)
        {
            _saved.Clear();
            _refused.Clear();
        }
    }

    /// <summary>
    /// Makes Entity Framework open a transaction for the save in progress, even for a single statement, so the save
    /// commits through <see cref="CacheTransactionInterceptor"/>; <see cref="RestoreAutoTransaction"/> puts it back.
    /// </summary>
    public void RequireTransaction(DbContext context)
    {
        if (context.Database.CurrentTransaction is not null
            || context.Database.AutoTransactionBehavior == AutoTransactionBehavior.Always)
        {
            return;
        }

        lock (_saved)
        {
            _autoTransaction ??= context.Database.AutoTransactionBehavior;
        }

        context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    /// <summary>
    /// The save has finished: the context opens transactions as it did before.
    /// </summary>
    public void RestoreAutoTransaction(DbContext context)
    {
        AutoTransactionBehavior? previous;

        lock (_saved)
        {
            previous = _autoTransaction;
            _autoTransaction = null;
        }

        if (previous is { } behavior)
        {
            context.Database.AutoTransactionBehavior = behavior;
        }
    }
}
