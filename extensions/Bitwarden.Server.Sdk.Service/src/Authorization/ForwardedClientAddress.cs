using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Takes the address a request came from, and the scheme it came over, as the proxy in front of the
/// service saw them, when that proxy is one it trusts, as <c>UseForwardedHeaders</c> with
/// <c>XForwardedFor | XForwardedProto</c> takes them.
/// </summary>
/// <remarks>
/// <para>
/// A request's address is its connection's. When the connection is a trusted proxy's (see
/// <see cref="TrustedProxies"/>) and the request carries <c>X-Forwarded-For</c>, it is the last
/// address there instead: the one that proxy added, of whoever connected to it. Only that one, as
/// ASP.NET's forward limit of one takes it, so nothing a client wrote into the header itself is
/// believed. A request from anywhere else keeps its connection's address, whatever it says.
/// </para>
/// <para>
/// The scheme likewise: from a trusted proxy, the last <c>X-Forwarded-Proto</c>, when it is
/// <c>http</c> or <c>https</c>. A service behind a proxy that terminates TLS then knows its request
/// came over https, so a cookie whose policy is <c>SameAsRequest</c> (a session, antiforgery) is
/// marked <c>Secure</c>. A request from anywhere else keeps
/// its connection's scheme.
/// </para>
/// <para>
/// What a product service records as the client's address (<see cref="Bitwarden.Server.Sdk.Common.IContext.ClientIpAddress"/>),
/// and carries on to the services it calls; and what any service's own view of the connection is.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="proxies">Whose word is taken.</param>
internal sealed class ForwardedClientAddress(RequestDelegate next, TrustedProxies proxies)
{
    /// <summary>
    /// The header a proxy names the address it was connected from in.
    /// </summary>
    internal const string ForwardedFor = "X-Forwarded-For";

    /// <summary>
    /// The header a proxy names the scheme it was connected over in.
    /// </summary>
    internal const string ForwardedProto = "X-Forwarded-Proto";

    public async Task InvokeAsync(HttpContext http)
    {
        var client = http.Request.Headers.TryGetValue(ForwardedFor, out var forwarded) ? Nearest(forwarded) : null;
        var scheme = http.Request.Headers.TryGetValue(ForwardedProto, out var proto) ? Scheme(proto) : null;

        if (http.Connection.RemoteIpAddress is { } peer
            && (client is not null || scheme is not null)
            && await proxies.TrustsAsync(peer, http.RequestAborted))
        {
            if (client is not null)
            {
                http.Connection.RemoteIpAddress = TrustedProxies.Canonical(client);
            }

            if (scheme is not null)
            {
                http.Request.Scheme = scheme;
            }
        }

        await next(http);
    }

    /// <summary>
    /// The last scheme the header names, when it is http or https; null otherwise.
    /// </summary>
    private static string? Scheme(StringValues values) =>
        values.LastOrDefault()?.Split(',').LastOrDefault()?.Trim().ToLowerInvariant() switch
        {
            "https" => "https",
            "http" => "http",
            _ => null,
        };

    /// <summary>
    /// The last address the header names, the nearest proxy's view, with or without a port; null
    /// when it is not an address.
    /// </summary>
    private static IPAddress? Nearest(StringValues values)
    {
        var last = values.LastOrDefault()?.Split(',').LastOrDefault()?.Trim();

        if (IPAddress.TryParse(last, out var address))
        {
            return address;
        }

        return IPEndPoint.TryParse(last ?? string.Empty, out var endpoint) ? endpoint.Address : null;
    }
}
