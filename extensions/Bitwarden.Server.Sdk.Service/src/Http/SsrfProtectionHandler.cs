using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// A <see cref="DelegatingHandler"/> that protects against Server-Side Request Forgery (SSRF)
/// by resolving hostnames to IP addresses before connecting and blocking requests
/// to internal/private/reserved IP ranges.
///
/// This handler performs DNS resolution on the request URI, validates that none of the
/// resolved addresses are internal, and then rewrites the request to connect directly
/// to the validated IP while preserving the original Host header for TLS/SNI.
///
/// It also handles HTTP redirects manually so that each redirect hop is validated
/// against SSRF rules. AllowAutoRedirect must be off on the primary handler; a response
/// that a handler below followed a redirect to is refused rather than returned.
/// </summary>
internal class SsrfProtectionHandler : DelegatingHandler
{
    private const int _maxRedirects = 10;

    private static readonly HashSet<HttpStatusCode> _redirectStatusCodes =
    [
        HttpStatusCode.MovedPermanently,   // 301
        HttpStatusCode.Found,              // 302
        HttpStatusCode.SeeOther,           // 303
        HttpStatusCode.TemporaryRedirect,  // 307
        (HttpStatusCode)308                // 308 Permanent Redirect
    ];

    private readonly ILogger<SsrfProtectionHandler> _logger;

    /// <summary>
    /// When <c>true</c> (default), the handler follows HTTP redirects and validates each hop
    /// against SSRF rules. When <c>false</c>, the handler validates the initial request only
    /// and returns redirect responses as-is to the caller. Set to <c>false</c> for clients
    /// that implement their own redirect-following logic (e.g., Icons).
    /// </summary>
    public bool FollowRedirects { get; set; } = true;

    public SsrfProtectionHandler(ILogger<SsrfProtectionHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
        {
            throw new SsrfProtectionException("Request URI is null.");
        }

        // Track the current URI and method across hops. ValidateAndSendAsync rewrites
        // the request URI to a resolved IP, so we must preserve the original hostname-based
        // URI for correct relative redirect resolution and the current method for correct
        // method transitions (e.g., POST→301→GET→307 should preserve GET, not the original POST).
        var currentUri = request.RequestUri!;
        var currentMethod = request.Method;

        // A redirect keeping the method sends the body again, and a body read from a stream can be sent only once:
        // buffered here, it can be read for each hop.
        if (FollowRedirects && request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        var currentContent = request.Content;

        var response = await ValidateAndSendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!FollowRedirects)
        {
            return response;
        }

        // Manually follow redirects with SSRF validation on each hop
        var redirectCount = 0;
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

            // Determine the method for the redirected request
            var redirectMethod = GetRedirectMethod(currentMethod, response.StatusCode);

            // A redirect that keeps the method keeps the body; one that changes it to GET leaves it behind. The
            // original request's headers (Authorization among them) are never carried to the next hop.
            var redirectContent = redirectMethod == currentMethod && currentContent is not null
                ? await CopyAsync(currentContent, cancellationToken).ConfigureAwait(false)
                : null;

            response.Dispose();

            using var redirectRequest = new HttpRequestMessage(redirectMethod, redirectUri) { Content = redirectContent };
            response = await ValidateAndSendAsync(redirectRequest, cancellationToken).ConfigureAwait(false);

            // Update tracking for next iteration: use the pre-rewrite URI
            // (redirectUri is not mutated by ValidateAndSendAsync)
            currentUri = redirectUri;
            currentMethod = redirectMethod;
            currentContent = redirectContent;
        }

        return response;
    }

    /// <summary>
    /// Validates the request URI against SSRF rules and sends the request via the inner handler.
    /// </summary>
    private async Task<HttpResponseMessage> ValidateAndSendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var host = uri.Host;

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

        // Rewrite the request URI to use the IP directly, preserving the Host header
        var builder = new UriBuilder(uri)
        {
            Host = selectedIp.ToString()
        };

        // Preserve the original Host header for TLS SNI and virtual hosting
        if (!request.Headers.Contains("Host"))
        {
            request.Headers.Host = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
        }

        var sent = builder.Uri;
        request.RequestUri = sent;

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // A handler below that followed a redirect itself (a primary handler still allowing automatic redirects)
        // answers for a destination this guard never checked: refuse the answer rather than hand it back.
        if (request.RequestUri != sent || (response.RequestMessage ?? request).RequestUri != sent)
        {
            response.Dispose();
            _logger.LogWarning("SSRF protection refused a response from {Host} that a handler below it redirected", host);
            throw new SsrfProtectionException(
                $"The request to '{host}' was redirected below SSRF protection; automatic redirects must be off on the primary handler.");
        }

        return response;
    }

    /// <summary>
    /// A copy of a buffered body, with its content headers, for the next hop of a redirect.
    /// </summary>
    private static async Task<HttpContent> CopyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var copy = new ByteArrayContent(await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        foreach (var header in content.Headers)
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return copy;
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

        // 303 always changes to GET per RFC 7231
        if (statusCode == HttpStatusCode.SeeOther)
        {
            return HttpMethod.Get;
        }

        // 301, 302: only POST changes to GET (historical browser behavior codified in RFC 7231)
        if (originalMethod == HttpMethod.Post)
        {
            return HttpMethod.Get;
        }

        return originalMethod;
    }

    /// <summary>
    /// Resolves a hostname to its IP addresses. If the host is already an IP address,
    /// returns it directly after validation.
    /// </summary>
    private static async Task<IPAddress[]> ResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        // If the host is already an IP address, validate and return it directly
        if (IPAddress.TryParse(host, out var directIp))
        {
            return [directIp];
        }

        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return [];
        }
    }
}
