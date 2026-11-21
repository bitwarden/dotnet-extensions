using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that protects against Server-Side Request Forgery (SSRF)
/// by resolving hostnames to IP addresses before connecting and blocking requests
/// to internal/private/reserved IP ranges.
/// </summary>
/// <remarks>
/// <para>
/// For each request it resolves the host, refuses the request when any address is internal or none resolves, and
/// records the address it checked on the request (<see cref="SsrfConnector.PinKey"/>), where
/// <see cref="SsrfConnector"/> connects to it. The request keeps its host, so TLS, cookies and telemetry see the name.
/// A request that goes through a proxy is addressed to the checked address instead, with the host in the
/// <c>Host</c> header, so the proxy has no name to resolve differently; once the response arrives, the request names
/// the host again.
/// </para>
/// <para>
/// It also handles HTTP redirects manually so that each redirect hop is validated
/// against SSRF rules. A redirect from https to http is returned, not followed.
/// <see cref="SsrfPrimaryHandlerFilter"/> puts it below every handler the client adds and sets <see cref="Sender"/>;
/// it refuses to send until that is done, and again if the primary handler was replaced or changed since.
/// </para>
/// </remarks>
internal class SsrfProtectionHandler : DelegatingHandler
{
    private const int _maxRedirects = 10;

    // The statuses SocketsHttpHandler follows.
    private static readonly HashSet<HttpStatusCode> _redirectStatusCodes =
    [
        HttpStatusCode.MultipleChoices,    // 300
        HttpStatusCode.MovedPermanently,   // 301
        HttpStatusCode.Found,              // 302
        HttpStatusCode.SeeOther,           // 303
        HttpStatusCode.TemporaryRedirect,  // 307
        (HttpStatusCode)308                // 308 Permanent Redirect
    ];

    private readonly ILogger<SsrfProtectionHandler> _logger;
    private readonly SsrfConnector _network;

    /// <summary>
    /// When <c>true</c> (default), the handler follows HTTP redirects and validates each hop
    /// against SSRF rules. When <c>false</c>, the handler validates the initial request only
    /// and returns redirect responses as-is to the caller. Set to <c>false</c> for clients
    /// that implement their own redirect-following logic.
    /// </summary>
    public bool FollowRedirects { get; set; } = true;

    /// <summary>
    /// The handler that sends this guard's requests, set up by <see cref="SsrfPrimaryHandlerFilter"/>.
    /// </summary>
    internal HttpMessageHandler? Sender { get; set; }

    /// <summary>
    /// A handler that resolves host names with <paramref name="network"/>, which also opens the connections.
    /// </summary>
    public SsrfProtectionHandler(ILogger<SsrfProtectionHandler> logger, SsrfConnector network)
    {
        _logger = logger;
        _network = network;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
        {
            throw new SsrfProtectionException("Request URI is null.");
        }

        EnsureSender();

        // HTTP/3 runs over QUIC, whose connections SocketsHttpHandler opens without its ConnectCallback, so the
        // connection couldn't be pinned to the checked address.
        if (request.Version.Major >= 3 || request.VersionPolicy == HttpVersionPolicy.RequestVersionOrHigher)
        {
            throw new SsrfProtectionException(
                "A request that may use HTTP/3 can't be guarded: ask for HTTP/2 or lower, or exactly the version asked for.");
        }

        // Track the current URI and method across hops: the URI each hop names, for correct relative redirect
        // resolution, and the current method for correct method transitions (e.g., POST→301→GET→307 should preserve
        // GET, not the original POST).
        var currentUri = request.RequestUri!;
        var currentMethod = request.Method;

        // The first hop is checked before anything is buffered, so a refused request reads none of its body.
        var pin = await CheckAsync(currentUri, cancellationToken).ConfigureAwait(false);

        // A redirect keeping the method sends the body again, and a body read from a stream can be sent only once:
        // the content is buffered, its bytes and content headers are kept here, and every hop that keeps the method
        // gets fresh content built from them (each hop's request, and its content, is disposed once the next hop
        // replaces it). The body is held twice in memory: the request's buffer and the copy.
        byte[]? body = null;
        KeyValuePair<string, IEnumerable<string>>[] bodyHeaders = [];
        if (FollowRedirects && request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            bodyHeaders = [.. request.Content.Headers];
        }

        var response = await SendCheckedAsync(request, pin, cancellationToken).ConfigureAwait(false);

        if (!FollowRedirects)
        {
            return response;
        }

        // Manually follow redirects with SSRF validation on each hop. The last hop's request is left undisposed:
        // the response returned refers to it as its RequestMessage.
        var redirectCount = 0;
        HttpRequestMessage? hop = null;
        while (_redirectStatusCodes.Contains(response.StatusCode) &&
               response.Headers.Location is not null &&
               redirectCount < _maxRedirects)
        {
            redirectCount++;

            var redirectUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(currentUri, response.Headers.Location);

            // Only allow http/https redirects
            if (redirectUri.Scheme != Uri.UriSchemeHttp && redirectUri.Scheme != Uri.UriSchemeHttps)
            {
                _logger.LogWarning(
                    "SSRF protection blocked redirect to non-HTTP scheme {Scheme}",
                    redirectUri.Scheme);
                break;
            }

            // Never from https to http: the next hop would leave TLS, and a 307 or 308 would send the body again in the
            // clear. As SocketsHttpHandler does, the redirect is returned unfollowed.
            if (currentUri.Scheme == Uri.UriSchemeHttps && redirectUri.Scheme == Uri.UriSchemeHttp)
            {
                _logger.LogWarning("SSRF protection did not follow a redirect from https to http");
                break;
            }

            // Determine the method for the redirected request
            var redirectMethod = GetRedirectMethod(currentMethod, response.StatusCode);

            // A redirect that keeps the method keeps the body; one that changes it to GET leaves it behind. The
            // original request's headers (Authorization among them) are never carried to the next hop.
            if (redirectMethod != currentMethod)
            {
                body = null;
            }

            response.Dispose();
            hop?.Dispose();

            hop = new HttpRequestMessage(redirectMethod, redirectUri)
            {
                Content = body is null ? null : Content(body, bodyHeaders),
            };

            try
            {
                response = await SendCheckedAsync(
                    hop, await CheckAsync(redirectUri, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                hop.Dispose();
                throw;
            }

            // Update tracking for next iteration: use the URI the hop names (redirectUri is not mutated by
            // SendCheckedAsync)
            currentUri = redirectUri;
            currentMethod = redirectMethod;
        }

        return response;
    }

    /// <summary>
    /// Refuses a request the guard can't vouch for: one it wasn't set up for, or whose primary handler was replaced or
    /// changed after <see cref="SsrfPrimaryHandlerFilter"/> set it up.
    /// </summary>
    private void EnsureSender()
    {
        if (Sender is null)
        {
            throw new InvalidOperationException("SSRF protection has no primary handler set up; add it with AddSsrfProtection.");
        }

        var handler = InnerHandler;
        while (!ReferenceEquals(handler, Sender) && handler is DelegatingHandler { InnerHandler: var inner })
        {
            handler = inner;
        }

        if (!ReferenceEquals(handler, Sender) ||
            Sender is SocketsHttpHandler { AllowAutoRedirect: true } or SocketsHttpHandler { UseCookies: true } ||
            Sender is SocketsHttpHandler { UseProxy: true, Proxy: null } ||
            Sender is SocketsHttpHandler socketsHttpHandler && socketsHttpHandler.ConnectCallback?.Target is not SsrfConnection)
        {
            throw new InvalidOperationException(
                "SSRF protection's primary handler was replaced or changed after it was set up.");
        }
    }

    /// <summary>
    /// Resolves the request URI's host and checks every address it resolves to, returning the one to connect to.
    /// </summary>
    private async Task<SsrfPin> CheckAsync(Uri uri, CancellationToken cancellationToken)
    {
        // A name in its ASCII (punycode) form, as DNS and the Host header need it: SocketsHttpHandler refuses a header
        // that isn't ASCII. An IP address keeps its brackets, as a Host header needs them for IPv6.
        var host = uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host;

        // Resolve the host to IP addresses
        var resolvedAddresses = await ResolveHostAsync(host, cancellationToken).ConfigureAwait(false);

        if (resolvedAddresses.Length == 0)
        {
            throw new SsrfProtectionException($"Unable to resolve DNS for host: {host}");
        }

        // Validate ALL resolved addresses; block if any are internal
        foreach (var address in resolvedAddresses)
        {
            var ipToCheck = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            if (ipToCheck.IsInternal())
            {
                _logger.LogWarning(
                    "SSRF protection blocked request to {Host} resolving to internal IP {Address}",
                    host,
                    ipToCheck);
                throw new SsrfProtectionException(
                    $"Request to '{host}' was blocked because it resolves to an internal IP address.");
            }
        }

        // Pick the first valid IPv4 address (prefer IPv4 for compatibility)
        var selectedIp = resolvedAddresses
            .Select(a => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? resolvedAddresses.First();

        return new SsrfPin(host.Trim('[', ']'), uri.Port, selectedIp, ThroughProxy(uri));
    }

    /// <summary>
    /// Sends a request whose address <see cref="CheckAsync"/> checked via the inner handler, pinned to that address.
    /// </summary>
    private async Task<HttpResponseMessage> SendCheckedAsync(
        HttpRequestMessage request,
        SsrfPin pin,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        request.Options.Set(SsrfConnector.PinKey, pin);

        // A request through a proxy is addressed to the checked IP, preserving the Host header for TLS SNI and
        // virtual hosting; one sent directly keeps its host, and the connector connects to the address.
        var sent = uri;
        if (pin.Proxied)
        {
            if (!request.Headers.Contains("Host"))
            {
                var host = uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host;
                request.Headers.Host = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
            }

            sent = new UriBuilder(uri) { Host = pin.Address.ToString() }.Uri;
            request.RequestUri = sent;
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.RequestUri = uri;
            throw;
        }

        // A handler below that readdressed the request, or followed a redirect itself, has sent it somewhere this
        // guard never checked. The request has already gone out by then; the answer is refused rather than handed back.
        if (request.RequestUri != sent || (response.RequestMessage ?? request).RequestUri != sent)
        {
            response.Dispose();
            _logger.LogWarning("SSRF protection refused a response from {Host} that a handler below it readdressed", pin.Host);
            throw new SsrfProtectionException(
                $"The request to '{pin.Host}' was readdressed below SSRF protection, and its response was refused.");
        }

        // A proxied request names the host again, so a caller resolving a relative Location against it, or against
        // the response's request, reaches the host it asked for.
        request.RequestUri = uri;
        if (response.RequestMessage is { } answered && answered.RequestUri == sent)
        {
            answered.RequestUri = uri;
        }

        return response;
    }

    /// <summary>
    /// Whether <see cref="Sender"/> sends a request for <paramref name="uri"/> through a proxy, as
    /// <see cref="SocketsHttpHandler"/> decides it: by the proxy <see cref="SsrfPrimaryHandlerFilter"/> fixed on it.
    /// </summary>
    private bool ThroughProxy(Uri uri) =>
        Sender is SocketsHttpHandler { UseProxy: true, Proxy: { } proxy } &&
        !proxy.IsBypassed(uri) &&
        proxy.GetProxy(uri) is not null;

    /// <summary>
    /// Fresh content for the next hop of a redirect, from the original body's bytes and content headers.
    /// </summary>
    private static ByteArrayContent Content(byte[] body, KeyValuePair<string, IEnumerable<string>>[] headers)
    {
        var content = new ByteArrayContent(body);
        foreach (var header in headers)
        {
            content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return content;
    }

    /// <summary>
    /// Determines the HTTP method to use for a redirect based on the original method and status code.
    /// </summary>
    private static HttpMethod GetRedirectMethod(HttpMethod originalMethod, HttpStatusCode statusCode)
    {
        // 307 and 308 preserve the original method
        if (statusCode is HttpStatusCode.TemporaryRedirect or (HttpStatusCode)308)
        {
            return originalMethod;
        }

        // 303 changes any method but GET and HEAD to GET, as SocketsHttpHandler does
        if (statusCode == HttpStatusCode.SeeOther)
        {
            return originalMethod == HttpMethod.Head ? HttpMethod.Head : HttpMethod.Get;
        }

        // 300, 301, 302: only POST changes to GET (historical browser behavior codified in RFC 7231)
        if (originalMethod == HttpMethod.Post)
        {
            return HttpMethod.Get;
        }

        return originalMethod;
    }

    /// <summary>
    /// Resolves a hostname to its IP addresses. If the host is already an IP address,
    /// returns it directly after validation. A name that can't be resolved, or is too long to be, resolves to none.
    /// </summary>
    private async Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        // If the host is already an IP address, validate and return it directly
        if (IPAddress.TryParse(host, out var directIp))
        {
            return [directIp];
        }

        try
        {
            return await _network.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentOutOfRangeException)
        {
            return [];
        }
    }
}
