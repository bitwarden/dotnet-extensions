namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Tries a call again, at most <see cref="BitwardenClientOptions.Retries"/> times, when the callee wasn't there:
/// a connection that failed, or a <c>502</c>, <c>503</c> or <c>504</c> from in front of it (<see cref="Transient"/>); or
/// when it is still running an earlier attempt of the same keyed write (a <c>409</c> with <c>Retry-After</c>).
/// </summary>
/// <remarks>
/// <para>
/// Only a call that is safe to send twice: a <c>GET</c> or <c>HEAD</c>, or a write carrying an <c>Idempotency-Key</c>,
/// which the callee does at most once and answers a repeat of from what it recorded
/// (<see cref="IdempotencyKeyHandler"/>, which every write passes first).
/// </para>
/// <para>
/// Waits between attempts double, with jitter (<see cref="BitwardenClientOptions.RetryDelay"/>, then twice it), so
/// the callers of a service that restarts don't all come back at once. All within the call's one deadline, which the
/// fault handler outside this one holds: a wait the deadline cuts short ends the call as the dependency not answering.
/// A failure past the last attempt is returned or thrown as it was, for the fault handler to translate. A failure of
/// Identity issuing the call's token, an open circuit, and every answer the service gave itself, are never retried.
/// </para>
/// </remarks>
/// <param name="retries">How many times to try again.</param>
/// <param name="delay">The first wait; each later one doubles it.</param>
/// <param name="time">The clock waits are kept by.</param>
internal sealed class RetryHandler(int retries, TimeSpan delay, TimeProvider time) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (retries <= 0 || !Repeatable(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // Sent more than once, so read once: content that streams (JsonContent) is serialized again on each send,
        // which a buffer makes the same bytes every time.
        if (request.Content is { } content)
        {
            await content.LoadIntoBufferAsync(cancellationToken);
        }

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;

            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException failure) when (attempt < retries && Transient.Is(failure))
            {
                await WaitAsync(attempt, cancellationToken);
                continue;
            }

            var stillRunning = Transient.StillRunning(request, response);

            if (attempt >= retries || !(stillRunning || Transient.Is(response)))
            {
                return response;
            }

            // An earlier attempt still running at the callee: wait as long as it asks (within reason), and the next
            // attempt gets the answer the first leaves.
            var asked = stillRunning ? Transient.RetryAfter(response, MaximumRetryAfter) : TimeSpan.Zero;
            response.Dispose();
            await WaitAsync(attempt, cancellationToken, asked);
        }
    }

    private static bool Repeatable(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get
        || request.Method == HttpMethod.Head
        || request.Headers.Contains(Bitwarden.Server.Sdk.Idempotency.IdempotencyKeys.Header);

    /// <summary>
    /// The longest <c>Retry-After</c> waited for.
    /// </summary>
    internal static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The wait before attempt <paramref name="attempt"/> + 2: the delay doubled per attempt, times 0.5 to 1.5, or what
    /// the callee asked if that is longer.
    /// </summary>
    private Task WaitAsync(int attempt, CancellationToken cancellationToken, TimeSpan asked = default)
    {
        var backOff = delay * Math.Pow(2, attempt) * (0.5 + Random.Shared.NextDouble());
        var wait = asked > backOff ? asked : backOff;

        return wait <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(wait, time, cancellationToken);
    }
}
