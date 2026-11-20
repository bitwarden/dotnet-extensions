using System.Security.Cryptography;
using System.Text;
using Bitwarden.Server.Sdk.Context;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Answers a service client's repeated <c>GET</c> or <c>HEAD</c> from the request memo (<see cref="RequestMemo"/>),
/// and makes the memo forget whenever the client sends anything else.
/// </summary>
/// <remarks>
/// <para>
/// Inside the context handler, so the key is exactly what is sent: the method, the address with its query (fields,
/// filters, paging), every header but the credentials, whether the context is delegated or asserted, and the calling
/// service. Outside the circuit breaker and the transport, so a remembered answer sends nothing and counts as no attempt.
/// Inside the fault handler and the retries, so a remembered answer is translated exactly as the first was, and an
/// attempt that failed is never remembered for the retry after it.
/// </para>
/// <para>
/// Kept, for the rest of the request: a <c>2xx</c> or <c>404</c> answer's status, headers and body bytes. Each caller is
/// given a new response over its own copy of them, so nothing it reads or disposes is shared. Never kept: any other
/// status, a timeout, a cancellation or a failure to connect, which each caller waiting on the same call at that moment
/// gets as its own. An answer whose body is larger than the memo's bound is passed through and not kept.
/// </para>
/// <para>
/// Any other method forgets everything the request remembered, before it is sent and again once it is answered or has
/// failed, so the request always sees its own writes and never keeps an answer read while one was in flight.
/// </para>
/// <para>
/// Never on Identity's token endpoint (whose calls go through their own client and don't pass here) nor any other
/// address under <c>/connect/</c>; never for a client that isn't a Bitwarden service's.
/// </para>
/// </remarks>
/// <param name="serviceName">The callee, as its client is registered: for the metrics.</param>
/// <param name="caller">This service's name as its credential gives it, for the key.</param>
/// <param name="configuration">Read for <see cref="RequestMemo.EnabledKey"/> on every call.</param>
/// <param name="product">
/// Whether the client is a product service's (<see cref="ProductClient"/>), which calls with the token it is given: then
/// only a request that is part of a product call is remembered, keyed by a digest of that token.
/// </param>
internal sealed class RequestMemoHandler(string serviceName, string caller, IConfiguration? configuration, bool product = false)
    : DelegatingHandler
{
    /// <summary>
    /// The memo handler that took a request, so another in the same pipeline passes it through.
    /// </summary>
    private static readonly HttpRequestOptionsKey<RequestMemoHandler> TakenBy = new("Bitwarden.RequestMemo.TakenBy");

    /// <summary>
    /// Headers left out of the key: the credentials, whose decision the key carries instead, and the trace context,
    /// which differs on every call.
    /// </summary>
    private static readonly HashSet<string> Unkeyed = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        BitwardenHeaders.Delegation,
        "traceparent",
        "tracestate",
        "baggage",
        "Request-Id",
        "Correlation-Context",
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(TakenBy, out var taker) && !ReferenceEquals(taker, this))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Options.Set(TakenBy, this);

        // A token request changes nothing a read could have seen.
        if (RequestMemo.Current is not { } memo
            || request.RequestUri is not { IsAbsoluteUri: true } address
            || address.AbsolutePath.Contains("/connect/", StringComparison.OrdinalIgnoreCase))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            // A write, or anything that might be one: what the request remembered may no longer be true, both while it
            // is in flight and once it has landed.
            memo.Clear();
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                memo.Clear();
            }
        }

        if (Switched(configuration)
            || (product && !ProductCallHandler.IsCall(request))
            || Key(request, address) is not { } key)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var (answer, outcome) = await memo.RememberAsync(
            key,
            OutgoingCall.IsFresh,
            token => ReadAsync(request, memo.MaxBytes, token),
            cancellationToken);

        RequestMemoMetrics.Counted(serviceName, RequestMemoMetrics.CallSource, outcome);

        return answer.Respond(request);
    }

    /// <summary>
    /// Whether the memo is switched off for a measurement (<see cref="RequestMemo.EnabledKey"/>).
    /// </summary>
    internal static bool Switched(IConfiguration? configuration) =>
        string.Equals(configuration?[RequestMemo.EnabledKey], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What identifies the call: null where the context handler didn't say whether it is delegated, so the call isn't
    /// remembered.
    /// </summary>
    private string? Key(HttpRequestMessage request, Uri address)
    {
        var key = new StringBuilder();
        key.Append(request.Method.Method).Append(' ').Append(address.AbsoluteUri).Append('\n');

        if (product)
        {
            // The person's token, as a digest: the key holds no credential, and two people's calls never meet.
            if (request.Headers.Authorization?.Parameter is not { Length: > 0 } bearer)
            {
                return null;
            }

            key.Append("product ").Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bearer))));
        }
        else
        {
            if (!request.Options.TryGetValue(ContextHandler.Asserted, out var asserted))
            {
                return null;
            }

            key.Append("service ").Append(caller).Append(asserted ? " asserted" : " delegated");
        }

        foreach (var header in request.Headers
            .Where(header => !Unkeyed.Contains(header.Key))
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase))
        {
            key.Append('\n').Append(header.Key.ToLowerInvariant()).Append(": ").AppendJoin(", ", header.Value);
        }

        return key.ToString();
    }

    private async Task<RequestMemoRead<MemoizedResponse>> ReadAsync(
        HttpRequestMessage request,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var status = (int)response.StatusCode;
        var kept = status is >= 200 and < 300 or 404;

        var answer = await MemoizedResponse.TakeAsync(response, request.Method == HttpMethod.Head, maxBytes, cancellationToken);

        return answer.Shareable
            ? new RequestMemoRead<MemoizedResponse>(answer, answer.Size, kept ? RequestMemoKeep.Keep : RequestMemoKeep.Share)
            : new RequestMemoRead<MemoizedResponse>(answer, 0, RequestMemoKeep.Alone);
    }
}
