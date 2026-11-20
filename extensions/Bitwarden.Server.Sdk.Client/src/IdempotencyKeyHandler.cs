using Bitwarden.Server.Sdk.Idempotency;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Gives a write to an operation the callee's contract marks as honouring keys (<see cref="KeyedOperations"/>) an
/// <c>Idempotency-Key</c>, once, before any attempt is sent: the operation's derived key inside an
/// <see cref="IdempotentOperation"/>, a fresh one outside, or the one the call site set itself.
/// </summary>
/// <remarks>
/// Outside the retries, so every attempt of one call carries the same key and the callee does the write at most once
/// (<see cref="RetryHandler"/>). Inside an operation the body is part of the key, so it is buffered here, as the
/// retries would buffer it anyway. A write to any other operation goes without a key, so it is retried only when it
/// never reached the callee.
/// </remarks>
/// <param name="keyed">The callee's operations that honour keys.</param>
/// <param name="context">
/// The context headers the call will carry, by name and value: whom the write is for. Asked only inside an operation,
/// whose key for the write is derived from them, so writes for different users or organizations get different keys.
/// </param>
internal sealed class IdempotencyKeyHandler(
    KeyedOperations keyed,
    Func<HttpRequestMessage, IEnumerable<KeyValuePair<string, string>>> context) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IsWrite(request.Method) && !request.Headers.Contains(IdempotencyKeys.Header) && keyed.Accepts(request.Method, request.RequestUri))
        {
            request.Headers.TryAddWithoutValidation(IdempotencyKeys.Header, await KeyForAsync(request, context, cancellationToken));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Whether a method changes something: anything but <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and <c>TRACE</c>.
    /// </summary>
    internal static bool IsWrite(HttpMethod method) =>
        method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options && method != HttpMethod.Trace;

    private static async Task<string> KeyForAsync(
        HttpRequestMessage request,
        Func<HttpRequestMessage, IEnumerable<KeyValuePair<string, string>>> context,
        CancellationToken cancellationToken)
    {
        if (IdempotentOperation.Current is null)
        {
            return Guid.NewGuid().ToString();
        }

        var body = Array.Empty<byte>();
        if (request.Content is { } content)
        {
            // Sent more than once, so read once: JsonContent serializes again on each read, which a buffer makes the
            // same bytes every time.
            await content.LoadIntoBufferAsync(cancellationToken);
            body = await content.ReadAsByteArrayAsync(cancellationToken);
        }

        return IdempotentOperation.KeyForWrite(
                request.Method.Method, request.RequestUri?.ToString() ?? string.Empty, body, context(request))
            ?? Guid.NewGuid().ToString();
    }
}
