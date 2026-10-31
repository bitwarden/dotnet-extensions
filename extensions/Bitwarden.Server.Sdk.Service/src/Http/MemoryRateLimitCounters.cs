using System.Collections.Concurrent;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Counts in this process's memory: the store used without a Redis.
/// </summary>
/// <remarks>
/// A window opens with a bucket's first request and lasts the period: a request after it opens a new
/// one. Windows past their period are swept out as counting goes on.
/// </remarks>
public sealed class MemoryRateLimitCounters : IRateLimitCounters, IDisposable
{
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private long _nextSweep;

    /// <summary>
    /// Counts by the system clock.
    /// </summary>
    public MemoryRateLimitCounters()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Counts by the given clock.
    /// </summary>
    /// <param name="time">The clock windows open and close by.</param>
    public MemoryRateLimitCounters(TimeProvider time)
    {
        _time = time;
        _nextSweep = time.GetUtcNow().Add(SweepEvery).UtcTicks;
    }

    /// <inheritdoc />
    public ValueTask<bool> TryTakeAsync(string bucket, int limit, TimeSpan period, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        Sweep(now);

        var window = _windows.AddOrUpdate(
            bucket,
            static (_, state) => new Window(state.Now + state.Period, 1),
            static (_, current, state) => state.Now >= current.Ends
                ? new Window(state.Now + state.Period, 1)
                : current with { Count = current.Count + 1 },
            (Now: now, Period: period));

        return ValueTask.FromResult(window.Count <= Math.Max(limit, 1));
    }

    /// <inheritdoc />
    public void Dispose() => _windows.Clear();

    private void Sweep(DateTimeOffset now)
    {
        var next = Interlocked.Read(ref _nextSweep);
        if (now.UtcTicks < next || Interlocked.CompareExchange(ref _nextSweep, now.Add(SweepEvery).UtcTicks, next) != next)
        {
            return;
        }

        foreach (var (bucket, window) in _windows)
        {
            if (now >= window.Ends)
            {
                _windows.TryRemove(new KeyValuePair<string, Window>(bucket, window));
            }
        }
    }

    private readonly record struct Window(DateTimeOffset Ends, int Count);
}
