using Microsoft.Extensions.Caching.Memory;

namespace Bitwarden.Server.Sdk.Service.Caching;

/// <summary>
/// A process-local cache of a downstream answer that concurrent requests resolve once, not once each.
/// </summary>
/// <remarks>
/// <para>
/// <c>IMemoryCache.GetOrCreateAsync</c> runs its factory for every caller that misses before the
/// first one finishes. A client that sends a sync's requests at the same time makes each of them
/// miss together, and each asks the downstream service the same question. This caches the
/// resolution itself, the task, so the second caller awaits the first's.
/// </para>
/// <para>
/// The lifetime and keys are the caller's; nothing is held longer or shared further than a plain
/// cache entry would be. A resolution that fails or is cancelled is not kept: the next caller
/// resolves again, rather than everybody inheriting one failure for the lifetime.
/// </para>
/// <para>
/// <b>No caller's cancellation token reaches the shared resolution.</b> One caller abandoning its
/// request must not fail the others waiting on the same answer; each caller stops waiting on its own
/// token instead, and the resolution finishes (bounded by the HTTP client's timeouts) for the rest.
/// </para>
/// </remarks>
public static class ResolvedOnce
{
    private static readonly object Gate = new();

    /// <summary>
    /// The cached answer, or one resolution shared by every caller waiting for it.
    /// </summary>
    /// <typeparam name="T">The answer.</typeparam>
    /// <param name="cache">The process's cache.</param>
    /// <param name="key">What the answer is about.</param>
    /// <param name="lifetime">How long an answer is held once resolved.</param>
    /// <param name="resolve">Asks for the answer; given no cancellation token, by design.</param>
    /// <param name="cancellationToken">Stops this caller waiting; does not cancel the resolution.</param>
    /// <returns>The answer.</returns>
    public static Task<T> GetOrResolveOnceAsync<T>(
        this IMemoryCache cache,
        object key,
        TimeSpan lifetime,
        Func<Task<T>> resolve,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(resolve);

        Task<T> pending;

        lock (Gate)
        {
            if (!cache.TryGetValue(key, out pending!) || pending.IsFaulted || pending.IsCanceled)
            {
                pending = resolve();
                cache.Set(key, pending, lifetime);
            }
        }

        return pending.WaitAsync(cancellationToken);
    }
}
