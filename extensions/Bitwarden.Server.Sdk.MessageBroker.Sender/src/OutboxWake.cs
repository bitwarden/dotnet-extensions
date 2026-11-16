using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Wakes a database's outbox dispatcher before its poll interval is up, for
/// <see cref="MessageOutboxOptions.SendOnCommit"/>.
/// </summary>
/// <remarks>
/// A signal raised while one is already waiting is merged into it, so a burst of commits wakes the
/// dispatcher once, and a lost signal costs only the wait for the next poll.
/// </remarks>
internal sealed class OutboxWake<TContext>
    where TContext : DbContext, IMessageOutboxContext
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>
    /// Asks the dispatcher to look at the outbox now.
    /// </summary>
    public void Signal()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled and not yet seen: the dispatcher will look once for both.
        }
    }

    /// <summary>
    /// Completes when signalled, consuming the signal.
    /// </summary>
    public Task WaitAsync(CancellationToken cancellationToken) => _signal.WaitAsync(cancellationToken);
}
