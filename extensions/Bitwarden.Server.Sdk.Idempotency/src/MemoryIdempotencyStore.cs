namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Keys held in this process: for a service with no shared Redis (self-hosted, development), where one instance
/// serves every call. Bounded, so a burst of writes can't grow it without limit: past
/// <see cref="MaximumEntries"/>, the expired go, then the answers soonest to expire.
/// </summary>
/// <remarks>
/// A write still running is never made room for: dropping its hold would let a repeat run it a second time. When
/// every key held is a write still running, a new claim is answered <see cref="Claim.Unavailable"/>, and the write
/// runs as it would without a key.
/// </remarks>
/// <param name="time">The clock expiries are kept by.</param>
internal sealed class MemoryIdempotencyStore(TimeProvider time) : IIdempotencyStore
{
    /// <summary>
    /// The most keys held.
    /// </summary>
    internal const int MaximumEntries = 10_000;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, (string Value, DateTimeOffset Expires, bool Running)> _entries = new(StringComparer.Ordinal);

    public ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();

            if (_entries.TryGetValue(key, out var held) && held.Expires > now)
            {
                return ValueTask.FromResult(Claim.Found(held.Value));
            }

            // An expired hold or answer makes way for this claim, and isn't counted while room is made.
            _entries.Remove(key);

            if (!Make(now))
            {
                return ValueTask.FromResult(Claim.Unavailable);
            }

            _entries[key] = (pending, now + lease, true);
            return ValueTask.FromResult(Claim.Held);
        }
    }

    public ValueTask CompleteAsync(string key, string answer, TimeSpan retention)
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (_entries.ContainsKey(key) || Make(now))
            {
                _entries[key] = (answer, now + retention, false);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAsync(string key, string pending)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var held) && held.Running && held.Value == pending)
            {
                _entries.Remove(key);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Room for one more: the expired go, then the answers soonest to expire. False when every key left is a write
    /// still running.
    /// </summary>
    private bool Make(DateTimeOffset now)
    {
        if (_entries.Count < MaximumEntries)
        {
            return true;
        }

        foreach (var expired in _entries.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToList())
        {
            _entries.Remove(expired);
        }

        if (_entries.Count >= MaximumEntries)
        {
            foreach (var oldest in _entries.Where(entry => !entry.Value.Running).OrderBy(entry => entry.Value.Expires)
                         .Take(_entries.Count - MaximumEntries + 1).Select(entry => entry.Key).ToList())
            {
                _entries.Remove(oldest);
            }
        }

        return _entries.Count < MaximumEntries;
    }
}
