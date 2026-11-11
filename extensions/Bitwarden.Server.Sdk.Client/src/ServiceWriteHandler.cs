using Bitwarden.Server.Sdk.Idempotency;
using Bitwarden.Server.Sdk.Sagas;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Reports each write this client sends to the operation running now (<see cref="ServiceWrites.Outgoing"/>), before it
/// is sent, so an operation whose writes span services is noticed, and refused where the host is set to, before the
/// write that makes it so.
/// </summary>
/// <remarks>
/// Once per call, inside the idempotency key and outside the retries: a call retried, or sent again by an operation's
/// retry loop with the same key, is one write. A write-method call that only reads, by its address
/// (<see cref="ReadingRoutes"/>) or as the client declares (<see cref="BitwardenClientOptions.ReadingPaths"/>), is not a
/// write.
/// </remarks>
/// <param name="serviceName">The callee, as the client's registration names it.</param>
/// <param name="readingPaths">The callee's write-method operations that only read.</param>
internal sealed class ServiceWriteHandler(string serviceName, IReadOnlyList<string> readingPaths) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Writes(request.Method, request.RequestUri, readingPaths))
        {
            ServiceWrites.Outgoing(
                serviceName,
                request.Headers.TryGetValues(IdempotencyKeys.Header, out var keys) ? keys.FirstOrDefault() : null);
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Whether a request is a write of the operation making it: a write method, to an address that neither
    /// <see cref="ReadingRoutes"/> nor one of <paramref name="readingPaths"/> says only reads.
    /// </summary>
    /// <param name="method">The request's method.</param>
    /// <param name="address">The request's address.</param>
    /// <param name="readingPaths">The callee's write-method operations that only read.</param>
    internal static bool Writes(HttpMethod method, Uri? address, IReadOnlyList<string> readingPaths) =>
        IdempotencyKeyHandler.IsWrite(method)
        && !ReadingRoutes.Reads(address)
        && !readingPaths.Any(path => EndsWith(address, path));

    private static bool EndsWith(Uri? address, string pattern)
    {
        if (address is null)
        {
            return false;
        }

        var text = address.IsAbsoluteUri ? address.AbsolutePath : address.OriginalString.Split('?')[0];
        var segments = text.Trim('/').Split('/');
        var wanted = pattern.Trim('/').Split('/');

        if (wanted.Length > segments.Length)
        {
            return false;
        }

        var offset = segments.Length - wanted.Length;
        for (var i = 0; i < wanted.Length; i++)
        {
            if (wanted[i] != "*" && !string.Equals(wanted[i], segments[offset + i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
