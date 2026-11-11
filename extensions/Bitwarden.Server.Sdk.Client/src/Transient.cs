using System.Net;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// What counts as the callee not being there, rather than the callee answering: what is retried, and what the circuit
/// breaker counts.
/// </summary>
internal static class Transient
{
    /// <summary>
    /// A connection that failed before any answer: refused, reset, a name that didn't resolve, or a response cut off.
    /// Never a TLS failure, which is configuration and fails the same way every time.
    /// </summary>
    public static bool Is(HttpRequestException exception) =>
        exception.HttpRequestError is HttpRequestError.ConnectionError
            or HttpRequestError.NameResolutionError
            or HttpRequestError.ResponseEnded
            or HttpRequestError.HttpProtocolError
            or HttpRequestError.ProxyTunnelError;

    /// <summary>
    /// A connection that never opened: refused, a name that didn't resolve, or a proxy that wouldn't tunnel. Nothing of
    /// the request reached the callee, so even a write without an <c>Idempotency-Key</c> can be sent again.
    /// </summary>
    public static bool NeverSent(HttpRequestException exception) =>
        exception.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ProxyTunnelError
        || (exception.HttpRequestError is HttpRequestError.ConnectionError
            && exception.InnerException is System.Net.Sockets.SocketException
            {
                SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused
                    or System.Net.Sockets.SocketError.HostUnreachable
                    or System.Net.Sockets.SocketError.NetworkUnreachable
                    or System.Net.Sockets.SocketError.HostNotFound
            });

    /// <summary>
    /// A <c>502</c>, <c>503</c> or <c>504</c> from something in front of the service (a proxy, a load balancer), not an
    /// answer of the service's own: it carries no JSON error document. A service's own <c>503</c> says what it is
    /// waiting for (a dependency down, a key rotation in progress) and is its answer; retrying it would multiply
    /// retries down a chain of calls.
    /// </summary>
    public static bool Is(HttpResponseMessage response)
    {
        var unavailable = response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        var servicesOwnAnswer = response.Content.Headers.ContentType?.MediaType?.EndsWith("json", StringComparison.OrdinalIgnoreCase) ?? false;

        return unavailable && !servicesOwnAnswer;
    }

    /// <summary>
    /// The callee still running an earlier attempt of this keyed write: a <c>409</c> with <c>Retry-After</c>
    /// (Bitwarden.Server.Sdk.Idempotency's keys). Waited for and tried again, for the answer the first attempt
    /// leaves; never the caller's conflict.
    /// </summary>
    public static bool StillRunning(HttpRequestMessage request, HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Conflict
        && response.Headers.RetryAfter is not null
        && request.Headers.Contains(Bitwarden.Server.Sdk.Idempotency.IdempotencyKeys.Header);

    /// <summary>
    /// How long a <c>Retry-After</c> asks to wait, at most <paramref name="limit"/>.
    /// </summary>
    public static TimeSpan RetryAfter(HttpResponseMessage response, TimeSpan limit)
    {
        var after = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - DateTimeOffset.UtcNow,
            _ => TimeSpan.Zero,
        };

        return after < TimeSpan.Zero ? TimeSpan.Zero : after > limit ? limit : after;
    }
}
