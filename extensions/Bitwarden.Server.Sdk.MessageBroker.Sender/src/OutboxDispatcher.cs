using System.Diagnostics;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Sends what is waiting in <typeparamref name="TContext"/>'s outbox, and deletes each message
/// once the broker has it. Any number of instances can run against one outbox: each claims a
/// batch with a lease, and a claim left by an instance that stopped is taken over once it expires.
/// A message is sent at least once, and again under the same identifier after a crash between the
/// send and the delete, or when one send outlasts the lease and another instance claims it.
/// </summary>
internal sealed class OutboxDispatcher<TContext> : BackgroundService
    where TContext : DbContext, IMessageOutboxContext
{
    internal static readonly ActivitySource Activities = new("Bitwarden.Server.Sdk.MessageBroker.Sender");

    /// <summary>
    /// How long a row the dispatcher claimed but has no route for waits before it is claimed again.
    /// </summary>
    private static readonly TimeSpan ReleaseDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceProvider _services;
    private readonly IOptions<MessageOutboxOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboxDispatcher<TContext>> _logger;
    private readonly OutboxMetrics _metrics;
    private readonly OutboxBacklog<TContext> _backlog;
    private readonly string _database = typeof(TContext).Name;
    private readonly Dictionary<(string Destination, string MessageType), OutboxRoute> _routes;
    private readonly Expression<Func<OutboxMessage, bool>> _routed;
    private readonly OutboxWake<TContext> _wake;
    private readonly IOutboxSendObserver[] _observers;

    public OutboxDispatcher(
        IServiceProvider services,
        IOptions<MessageOutboxOptions> options,
        TimeProvider time,
        ILogger<OutboxDispatcher<TContext>> logger,
        IEnumerable<OutboxRegistration> registrations,
        OutboxMetrics metrics,
        OutboxBacklog<TContext> backlog,
        OutboxWake<TContext> wake,
        IEnumerable<IOutboxSendObserver> observers)
    {
        _wake = wake;
        _observers = [.. observers];
        _metrics = metrics;
        _backlog = backlog;
        _services = services;
        _options = options;
        _time = time;
        _logger = logger;
        _routes = registrations
            .Where(r => r.Context == typeof(TContext))
            .Select(r => r.Route)
            .DistinctBy(r => (r.Destination, r.MessageType))
            .ToDictionary(r => (r.Destination, r.MessageType));
        _routed = Routed(_routes.Keys);
    }

    /// <summary>
    /// How long before its lease runs out the dispatcher stops starting sends from a batch: a quarter
    /// of the lease, 7.5 seconds by default. That is longer than a send to a broker that doesn't
    /// answer takes to fail (RabbitMQ's connection attempt gives up after five seconds), so a send
    /// started before it normally ends while the claim still holds, and another instance never
    /// claims a row this one is still sending.
    /// </summary>
    private TimeSpan LeaseMargin => Options.Lease / 4;

    /// <summary>
    /// <c>row =&gt; (row.Destination == d1 &amp;&amp; row.MessageType == t1) || ...</c>, one term per
    /// route: only a registered pairing of destination and type, never another destination's type.
    /// Built as an expression, so it becomes SQL on every engine.
    /// </summary>
    private static Expression<Func<OutboxMessage, bool>> Routed(IEnumerable<(string Destination, string MessageType)> routes)
    {
        // The members come from lambdas, not looked up by name, so trimming keeps them.
        Expression<Func<OutboxMessage, string>> destinationOf = row => row.Destination;
        Expression<Func<OutboxMessage, string>> messageTypeOf = row => row.MessageType;
        var row = destinationOf.Parameters[0];
        var destination = destinationOf.Body;
        var messageType = Expression.MakeMemberAccess(row, ((MemberExpression)messageTypeOf.Body).Member);
        Expression? body = null;
        foreach (var (d, t) in routes)
        {
            var route = Expression.AndAlso(
                Expression.Equal(destination, Expression.Constant(d)),
                Expression.Equal(messageType, Expression.Constant(t)));
            body = body is null ? route : Expression.OrElse(body, route);
        }
        return Expression.Lambda<Func<OutboxMessage, bool>>(body ?? Expression.Constant(false), row);
    }

    private MessageOutboxOptions Options => _options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Options.Dispatch)
        {
            _logger.LogInformation("Not dispatching {Context}'s outbox: MessageOutbox:Dispatch is off.", typeof(TContext).Name);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed;
            try
            {
                claimed = await DispatchAsync(stoppingToken);
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                // A database client can report cancellation as its own exception (SqlClient does).
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read {Context}'s outbox.", typeof(TContext).Name);
                claimed = 0;
            }

            try
            {
                await _backlog.SampleAsync(stoppingToken);
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not count {Context}'s outbox.", typeof(TContext).Name);
            }

            if (claimed >= Options.BatchSize)
                continue;
            try
            {
                await WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Waits for the poll interval, or until woken, whichever comes first.
    /// </summary>
    /// <remarks>
    /// Only <see cref="MessageOutboxOptions.SendOnCommit"/> wakes it. The poll stays as the backstop:
    /// without a signal this is the plain delay.
    /// </remarks>
    private async Task WaitAsync(CancellationToken stoppingToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var poll = Task.Delay(Options.PollInterval, _time, waiting.Token);
        var woken = _wake.WaitAsync(waiting.Token);

        await Task.WhenAny(poll, woken);
        await waiting.CancelAsync();

        // Observe whichever lost, so its cancellation is not left unobserved.
        await Task.WhenAll(poll, woken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        stoppingToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Claims, sends and deletes one batch. Returns how many of the messages it claimed it has a
    /// route for, whether it sent them or left them for the next claim.
    /// </summary>
    internal async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>();
        var outbox = database.Set<OutboxMessage>();

        var token = Guid.NewGuid();
        var (claimed, leaseUntil) = await ClaimAsync(outbox, token, cancellationToken);
        var routed = 0;
        var sent = new List<string>();
        for (var i = 0; i < claimed.Count; i++)
        {
            var row = claimed[i];
            if (!_routes.TryGetValue((row.Destination, row.MessageType), out var route))
            {
                // The claim's filter takes only routed rows, so this is a fallback. Another build,
                // older or newer during a deploy, may send it: leave it for that one, a little later,
                // so it doesn't head every claim here.
                await PostponeAsync(outbox, row.MessageId, token, cancellationToken);
                continue;
            }
            routed++;

            // Too little of the lease is left to start another send: leave the rest of the batch for
            // the next claim, this instance's or another's, rather than send a row another instance
            // may already be able to claim.
            if (_time.GetUtcNow() >= leaseUntil - LeaseMargin)
            {
                var rest = claimed.Skip(i).Select(each => each.MessageId).ToList();
                await ReleaseAsync(outbox, rest, token, cancellationToken);
                routed += claimed.Skip(i + 1).Count(each => _routes.ContainsKey((each.Destination, each.MessageType)));
                break;
            }

            try
            {
                var toBroker = route.Destination != MessageOutbox.Local;
                if (toBroker)
                    foreach (var observer in _observers)
                        observer.Sending(row);
                await SendAsync(route, row, cancellationToken);
                if (toBroker)
                    foreach (var observer in _observers)
                        observer.Sent(row);
                sent.Add(row.MessageId);
                _metrics.Sent(_database, row, _time.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(row.DueAt));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await DeferAsync(outbox, row, token, ex, cancellationToken);
            }
        }

        if (sent.Count > 0)
        {
            // Only rows this batch still holds: a send slower than the lease lets another instance
            // claim a row, and that instance's claim is now the one that settles it.
            await outbox.Where(row => sent.Contains(row.MessageId) && row.LeaseToken == token)
                .ExecuteDeleteAsync(cancellationToken);
        }
        return routed;
    }

    private static async Task SendAsync(OutboxRoute route, OutboxMessage row, CancellationToken cancellationToken)
    {
        // Continue the trace of the code that sent it, so the time it waited shows in that trace.
        ActivityContext parent = default;
        if (row.TraceParent is not null)
            ActivityContext.TryParse(row.TraceParent, null, isRemote: true, out parent);
        using var activity = Activities.StartActivity($"{row.Destination} outbox", ActivityKind.Internal, parent);
        await route.SendAsync(row, cancellationToken);
    }

    private async Task<(List<OutboxMessage> Claimed, DateTimeOffset LeaseUntil)> ClaimAsync(
        DbSet<OutboxMessage> outbox, Guid token, CancellationToken cancellationToken)
    {
        var start = _time.GetUtcNow();
        var now = start.ToUnixTimeMilliseconds();
        var until = now + (long)Options.Lease.TotalMilliseconds;

        // Only messages this build can send, by destination and type together. During a rolling
        // deploy a newer build writes types an older one doesn't know; the older one must leave
        // them for the newer.
        var candidates = await outbox.AsNoTracking()
            .Where(row => row.SetAsideAt == null && row.AvailableAt <= now && (row.LeaseUntil == null || row.LeaseUntil < now))
            .Where(_routed)
            .OrderBy(row => row.AvailableAt).ThenBy(row => row.MessageId)
            .Select(row => row.MessageId)
            .Take(Options.BatchSize)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
            return ([], start);

        // Repeats the test, so a row another instance claimed since it was read is left alone.
        await outbox
            .Where(row => candidates.Contains(row.MessageId) && row.SetAsideAt == null && (row.LeaseUntil == null || row.LeaseUntil < now))
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.LeaseToken, token)
                .SetProperty(row => row.LeaseUntil, until), cancellationToken);

        var claimed = await outbox.AsNoTracking()
            .Where(row => row.LeaseToken == token)
            .OrderBy(row => row.AvailableAt).ThenBy(row => row.MessageId)
            .ToListAsync(cancellationToken);
        return (claimed, DateTimeOffset.FromUnixTimeMilliseconds(until));
    }

    private async Task DeferAsync(DbSet<OutboxMessage> outbox, OutboxMessage row, Guid token, Exception exception, CancellationToken cancellationToken)
    {
        var attempts = row.Attempts + 1;
        var error = Describe(exception);
        _metrics.Failed(_database, row, exception);
        // A committed message must never be lost, so a failure is retried for as long as it takes:
        // an outage, a missing queue, expired credentials and a missing permission all clear. Only
        // a failure that sending again can never fix sets it aside.
        if (OutboxFailures.IsPermanent(exception))
        {
            _logger.LogError(exception,
                "Could not send message {MessageId} to {Destination}, and sending it again can't succeed; setting it aside.",
                row.MessageId, row.Destination);
            await SetAsideAsync(outbox, row.MessageId, attempts, token, error, cancellationToken);
            _metrics.SetAside(_database, row);
            return;
        }

        var backoff = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Min(attempts, 30)), Options.MaximumBackoff.TotalSeconds));
        var availableAt = _time.GetUtcNow().Add(backoff).ToUnixTimeMilliseconds();
        _logger.LogWarning(exception,
            "Could not send message {MessageId} to {Destination}; attempt {Attempts}, trying again in {Backoff}.",
            row.MessageId, row.Destination, attempts, backoff);
        await outbox.Where(each => each.MessageId == row.MessageId && each.LeaseToken == token)
            .ExecuteUpdateAsync(set => set
                .SetProperty(each => each.Attempts, attempts)
                .SetProperty(each => each.AvailableAt, availableAt)
                .SetProperty(each => each.LastError, error)
                .SetProperty(each => each.LeaseToken, (Guid?)null)
                .SetProperty(each => each.LeaseUntil, (long?)null), cancellationToken);
    }

    private async Task SetAsideAsync(DbSet<OutboxMessage> outbox, string messageId, int attempts, Guid token, string reason, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        var error = reason.Length > 200 ? reason[..200] : reason;
        await outbox.Where(each => each.MessageId == messageId && each.LeaseToken == token)
            .ExecuteUpdateAsync(set => set
                .SetProperty(each => each.Attempts, attempts)
                .SetProperty(each => each.SetAsideAt, now)
                .SetProperty(each => each.LastError, error)
                .SetProperty(each => each.LeaseToken, (Guid?)null)
                .SetProperty(each => each.LeaseUntil, (long?)null), cancellationToken);
    }

    // Gives rows back unclaimed, as they were, for the next claim.
    private static Task ReleaseAsync(DbSet<OutboxMessage> outbox, List<string> messageIds, Guid token, CancellationToken cancellationToken) =>
        outbox.Where(each => messageIds.Contains(each.MessageId) && each.LeaseToken == token)
            .ExecuteUpdateAsync(set => set
                .SetProperty(each => each.LeaseToken, (Guid?)null)
                .SetProperty(each => each.LeaseUntil, (long?)null), cancellationToken);

    // Gives a row back unclaimed and available again only after ReleaseDelay; its attempts and error
    // are untouched.
    private Task PostponeAsync(DbSet<OutboxMessage> outbox, string messageId, Guid token, CancellationToken cancellationToken)
    {
        var availableAt = _time.GetUtcNow().Add(ReleaseDelay).ToUnixTimeMilliseconds();
        return outbox.Where(each => each.MessageId == messageId && each.LeaseToken == token)
            .ExecuteUpdateAsync(set => set
                .SetProperty(each => each.AvailableAt, availableAt)
                .SetProperty(each => each.LeaseToken, (Guid?)null)
                .SetProperty(each => each.LeaseUntil, (long?)null), cancellationToken);
    }

    // The failure's type only: its message could carry the message's contents.
    private static string Describe(Exception exception)
    {
        var name = exception.GetType().FullName ?? exception.GetType().Name;
        return name.Length > 200 ? name[..200] : name;
    }
}
