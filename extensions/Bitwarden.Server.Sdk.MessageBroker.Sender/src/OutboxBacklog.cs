using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Counts one database's outbox, at most once per <see cref="MessageOutboxOptions.BacklogSampleInterval"/>
/// however often it is asked.
/// </summary>
/// <remarks>
/// Lateness is measured from <see cref="OutboxMessage.DueAt"/>, which retries don't move, so a
/// message that keeps failing grows older, and one sent for later isn't late while it waits.
/// </remarks>
internal sealed class OutboxBacklog<TContext>
    where TContext : DbContext, IMessageOutboxContext
{
    private readonly IServiceProvider _services;
    private readonly IOptions<MessageOutboxOptions> _options;
    private readonly TimeProvider _time;
    private OutboxBacklogSample? _last;

    public OutboxBacklog(IServiceProvider services, IOptions<MessageOutboxOptions> options, TimeProvider time, OutboxMetrics metrics)
    {
        _services = services;
        _options = options;
        _time = time;
        metrics.Register(typeof(TContext).Name, () => _last);
    }

    public async Task<OutboxBacklogSample> SampleAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (_last is { } last && now - last.TakenAt < _options.Value.BacklogSampleInterval)
            return last;

        await using var scope = _services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<TContext>().Set<OutboxMessage>().AsNoTracking();
        var nowMs = now.ToUnixTimeMilliseconds();
        var due = outbox.Where(row => row.SetAsideAt == null && row.DueAt <= nowMs);
        var count = await due.LongCountAsync(cancellationToken);
        var oldest = count == 0 ? null : await due.MinAsync(row => (long?)row.DueAt, cancellationToken);
        var setAside = await outbox.LongCountAsync(row => row.SetAsideAt != null, cancellationToken);
        return _last = new OutboxBacklogSample(
            count, oldest is { } at ? now - DateTimeOffset.FromUnixTimeMilliseconds(at) : null, setAside, now);
    }
}
