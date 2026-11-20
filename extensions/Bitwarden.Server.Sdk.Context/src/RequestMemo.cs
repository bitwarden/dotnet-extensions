using System.Collections.Concurrent;

namespace Bitwarden.Server.Sdk.Context;

/// <summary>
/// What one unit of work has already asked other services, remembered until it ends: the request-local memo.
/// </summary>
/// <remarks>
/// <para>
/// A request often asks another service the same question several times: whether the caller is a member, which groups
/// they are in, what the organization allows. The answer is the same for the rest of the request, so the platform asks
/// once and answers the repeats from memory. A service doesn't call this type to remember anything: every <c>GET</c>
/// and <c>HEAD</c> a service client (<c>Bitwarden.Server.Sdk.Client</c>) sends is remembered while a memo is current,
/// keyed by exactly what was sent, and the reads of the owners' caches beside them.
/// </para>
/// <para>
/// The service host begins one for each inbound HTTP request, unless the operation carries
/// <see cref="WithoutRequestMemoAttribute"/> or <see cref="EnabledKey"/> is <c>false</c>. Nothing else has one: a
/// message handler, an outbox handler, a job or a background service begins its own around one item of work with
/// <see cref="Begin()"/> where it wants one.
/// </para>
/// <para>
/// A memo forgets everything (<see cref="Forget"/>) when the work writes: a write sent to another service through its
/// client, and a save to the service's own database, so the work always sees its own writes. It is held in memory
/// only, and only for the unit of work: nothing in it outlives the request or reaches another caller.
/// </para>
/// <para>
/// Thread-safe: a request's parallel calls share it, and identical ones in flight at once share one call.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public async Task HandleAsync(WidgetChanged message, CancellationToken cancellationToken)
/// {
///     using var memo = RequestMemo.Begin();
///
///     // Both ask the Organization Service once between them.
///     await notifier.NotifyAsync(message.Organization, cancellationToken);
///     await auditor.RecordAsync(message.Organization, cancellationToken);
/// }
/// </code>
/// </example>
public sealed class RequestMemo : IDisposable
{
    /// <summary>
    /// The setting that switches the memo off (<c>false</c>), for measuring what it saves. On unless set to
    /// <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Off, every call is sent each time it is made, so one request may decide with two answers if something changes
    /// while it runs. For measurement only, never for a deployment.
    /// </remarks>
    public const string EnabledKey = "Caching:RequestMemo:Enabled";

    /// <summary>
    /// The setting for the most answers one memo keeps (<see cref="DefaultMaxEntries"/> unless set).
    /// </summary>
    public const string MaxEntriesKey = "Caching:RequestMemo:MaxEntries";

    /// <summary>
    /// The setting for the most bytes of answers one memo keeps (<see cref="DefaultMaxBytes"/> unless set).
    /// </summary>
    public const string MaxBytesKey = "Caching:RequestMemo:MaxBytes";

    /// <summary>
    /// The most answers a memo keeps unless configured: past it, calls are sent as if there were none.
    /// </summary>
    public const int DefaultMaxEntries = 256;

    /// <summary>
    /// The most bytes of answers a memo keeps unless configured (4 MiB): past it, calls are sent as if there were none.
    /// </summary>
    public const long DefaultMaxBytes = 4L * 1024 * 1024;

    private static readonly AmbientStack<RequestMemo> Ambient = new();

    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private long _bytes;
    private IDisposable? _scope;
    private RequestMemo? _outer;
    private volatile bool _ended;

    private RequestMemo(int maxEntries, long maxBytes)
    {
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    /// <summary>
    /// The memo of the work running now, if it has one and it hasn't ended.
    /// </summary>
    internal static RequestMemo? Current => Ambient.Current is { _ended: false } memo ? memo : null;

    /// <summary>
    /// The most bytes this memo keeps: an answer larger than this is never buffered to be kept.
    /// </summary>
    internal long MaxBytes => _maxBytes;

    /// <summary>
    /// Begins a memo for the work that follows, with the default bounds, until the returned memo is disposed.
    /// </summary>
    /// <remarks>
    /// For a message handler, an outbox handler, a job or a background service: begin one around one item of work (one
    /// message, one organization a job visits), never around a loop over many, so no item decides with what another
    /// read. An inbound HTTP request has one already. A memo begun inside another replaces it until disposed, and a write
    /// forgets what both remember.
    /// </remarks>
    /// <returns>The memo; disposing it ends it and restores whatever memo was current before.</returns>
    public static RequestMemo Begin() => Begin(DefaultMaxEntries, DefaultMaxBytes);

    /// <summary>
    /// Begins a memo for the work that follows, keeping at most <paramref name="maxEntries"/> answers and
    /// <paramref name="maxBytes"/> bytes of them, until the returned memo is disposed.
    /// </summary>
    /// <param name="maxEntries">The most answers kept; past it, calls are sent as if there were no memo.</param>
    /// <param name="maxBytes">The most bytes of answers kept; an answer that doesn't fit is not kept.</param>
    /// <returns>The memo; disposing it ends it and restores whatever memo was current before.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either bound is negative.</exception>
    public static RequestMemo Begin(int maxEntries, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var memo = new RequestMemo(maxEntries, maxBytes) { _outer = Current };
        memo._scope = Ambient.Push(memo);

        return memo;
    }

    /// <summary>
    /// Forgets everything the current memo remembers, if there is one.
    /// </summary>
    /// <remarks>
    /// The platform calls it on every write it sees: one sent through a service client, before it is sent and again
    /// when it is answered, and every save to the service's own database. Call it yourself only after a write the
    /// platform can't see that could change what another service answers.
    /// </remarks>
    public static void Forget() => Current?.Clear();

    /// <summary>
    /// Ends the memo: it forgets everything, and work still holding it (a task started inside it and not awaited)
    /// asks every call again.
    /// </summary>
    public void Dispose()
    {
        _ended = true;
        ForgetOwn();
        _scope?.Dispose();
    }

    /// <summary>
    /// Forgets everything remembered, here and in every memo this one was begun inside, since a write seen here may
    /// change what they remember too. A call already in flight still answers its waiters, and isn't kept.
    /// </summary>
    internal void Clear()
    {
        ForgetOwn();
        _outer?.Clear();
    }

    private void ForgetOwn()
    {
        lock (_gate)
        {
            _entries = new ConcurrentDictionary<string, Entry>(StringComparer.Ordinal);
            _bytes = 0;
        }
    }

    /// <summary>
    /// The answer to <paramref name="key"/>: the one remembered, the one a call in flight is getting, or a new one read
    /// now.
    /// </summary>
    /// <typeparam name="T">The answer's type; a remembered answer is shared, so it must never be changed.</typeparam>
    /// <param name="key">Exactly what was asked, and of whom, as whoever asked it.</param>
    /// <param name="fresh">Read again even if remembered, and remember the new answer in place of the old.</param>
    /// <param name="read">Reads the answer, and says whether it may be kept and shared.</param>
    /// <param name="cancellationToken">Cancels this caller's wait, and its own read.</param>
    /// <returns>The answer, and where it came from.</returns>
    /// <remarks>
    /// A read that fails fails every caller waiting on it, and nothing is kept, except that a caller whose own token
    /// wasn't cancelled reads for itself when the read it waited on was cancelled by somebody else's.
    /// </remarks>
    internal async Task<(T Value, RequestMemoOutcome Outcome)> RememberAsync<T>(
        string key,
        bool fresh,
        Func<CancellationToken, Task<RequestMemoRead<T>>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(read);

        while (true)
        {
            if (_ended)
            {
                return ((await read(cancellationToken)).Value, RequestMemoOutcome.Bypassed);
            }

            var entries = Volatile.Read(ref _entries);

            if (!fresh && entries.TryGetValue(key, out var found))
            {
                var settled = found.Answer.Task.IsCompleted;
                object? answer;
                try
                {
                    answer = await found.Answer.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Somebody else's call was cancelled, not this one: read for itself, keeping nothing.
                    return ((await read(cancellationToken)).Value, RequestMemoOutcome.Bypassed);
                }

                if (answer is Alone)
                {
                    // The answer could not be shared (a body too large to hold): read for itself.
                    return ((await read(cancellationToken)).Value, RequestMemoOutcome.Bypassed);
                }

                return ((T)answer!, settled ? RequestMemoOutcome.Hit : RequestMemoOutcome.Joined);
            }

            var entry = new Entry();
            bool full;
            bool taken;
            lock (_gate)
            {
                // Checked and inserted together, so parallel reads never take the memo past its bound. A fresh read of
                // an answer already held replaces it, so it never leaves the old answer behind for being at the bound.
                var held = entries.TryGetValue(key, out var replaced);
                full = !held && entries.Count >= _maxEntries;
                taken = held && !fresh;
                if (!full && !taken)
                {
                    entries[key] = entry;
                    if (replaced is not null)
                    {
                        Release(entries, replaced);
                    }
                }
            }

            if (full)
            {
                return ((await read(cancellationToken)).Value, RequestMemoOutcome.Bypassed);
            }

            if (taken)
            {
                // Somebody else began the same read a moment ago: wait on theirs.
                continue;
            }

            RequestMemoRead<T> result;
            try
            {
                result = await read(cancellationToken);
            }
            catch (Exception exception)
            {
                entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                entry.Answer.TrySetException(exception);

                // Observed here, so a failure nobody else waited on isn't reported as unobserved.
                _ = entry.Answer.Task.Exception;
                throw;
            }

            switch (result.Keep)
            {
                case RequestMemoKeep.Keep when Reserve(entries, key, entry, result.Size):
                    entry.Answer.TrySetResult(result.Value);
                    break;
                case RequestMemoKeep.Keep:
                case RequestMemoKeep.Share:
                    entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                    entry.Answer.TrySetResult(result.Value);
                    break;
                default:
                    entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
                    entry.Answer.TrySetResult(Alone.Instance);
                    break;
            }

            return (result.Value, RequestMemoOutcome.Miss);
        }
    }

    /// <summary>
    /// Counts an answer against the bound on bytes, if it fits, the memo hasn't forgotten since the read began, and no
    /// fresh read has replaced it.
    /// </summary>
    private bool Reserve(ConcurrentDictionary<string, Entry> entries, string key, Entry entry, long size)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(entries, _entries) || _ended || size < 0 || _bytes + size > _maxBytes
                || !entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
            {
                return false;
            }

            _bytes += size;
            entry.Size = size;
            return true;
        }
    }

    private void Release(ConcurrentDictionary<string, Entry> entries, Entry entry)
    {
        lock (_gate)
        {
            if (ReferenceEquals(entries, _entries))
            {
                _bytes -= entry.Size;
                entry.Size = 0;
            }
        }
    }

    private sealed class Entry
    {
        public readonly TaskCompletionSource<object?> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long Size;
    }

    /// <summary>
    /// The answer to waiters when the reader's answer could not be shared.
    /// </summary>
    private sealed class Alone
    {
        public static readonly Alone Instance = new();
    }
}
