using System.Net;
using System.Net.Sockets;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// The network as a guarded client sees it: resolving the names <see cref="SsrfProtectionHandler"/> checks, and
/// opening each connection to the address it checked for the request that asked for it, so a name can't resolve
/// elsewhere between the check and the connection.
/// </summary>
/// <remarks>
/// <see cref="SsrfPrimaryHandlerFilter"/> sets <see cref="ConnectAsync"/> as the primary handler's
/// <see cref="SocketsHttpHandler.ConnectCallback"/>. A connection the handler pools serves later requests to the same
/// host; each of those is checked again before it is sent.
/// </remarks>
internal class SsrfConnector
{
    /// <summary>
    /// The request option carrying the <see cref="SsrfPin"/> a request's connection must reach.
    /// </summary>
    internal static readonly HttpRequestOptionsKey<SsrfPin> PinKey = new("Bitwarden.Server.Sdk.Http.SsrfPin");

    /// <summary>
    /// The addresses <paramref name="host"/> resolves to. Throws <see cref="SocketException"/> for a name that can't
    /// be resolved, and <see cref="ArgumentOutOfRangeException"/> for one longer than DNS allows.
    /// </summary>
    public virtual Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);

    /// <summary>
    /// Connects for <paramref name="context"/>'s request: to the address checked for it when the connection is to the
    /// host the request names, or to the proxy when the request goes through one and still names the checked address.
    /// Any other connection is refused.
    /// </summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var request = context.InitialRequestMessage;
        var endpoint = context.DnsEndPoint;
        if (!request.Options.TryGetValue(PinKey, out var pin))
        {
            // An https request through a proxy reaches it through a tunnel, which SocketsHttpHandler asks for with a
            // CONNECT request of its own, unpinned, naming the request's host. The guard addressed that request to the
            // address it checked, so the tunnel is opened only to an address that isn't internal.
            if (request.Method == HttpMethod.Connect && TunnelsToPublicAddress(request))
            {
                return await OpenAsync(endpoint, cancellationToken).ConfigureAwait(false);
            }

            throw new SsrfProtectionException("A connection was asked for a request that SSRF protection did not check.");
        }

        if (endpoint.Port == pin.Port && Names(endpoint.Host, pin))
        {
            return await OpenAsync(new IPEndPoint(pin.Address, pin.Port), cancellationToken).ConfigureAwait(false);
        }

        // A proxied request names the checked address, so the proxy is given no name to resolve differently. One a
        // handler below the guard readdressed names something else, and is refused.
        if (pin.Proxied && request.RequestUri is { } uri && uri.Port == pin.Port && Names(uri.Host, pin))
        {
            return await OpenAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }

        throw new SsrfProtectionException(
            $"A connection for a request to '{pin.Host}' was asked for '{endpoint.Host}', which SSRF protection did not check.");
    }

    /// <summary>
    /// Opens a TCP connection to <paramref name="endpoint"/>, as <see cref="SocketsHttpHandler"/> does without a
    /// callback.
    /// </summary>
    protected virtual async ValueTask<Stream> OpenAsync(EndPoint endpoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Whether a tunnel request names, in its <c>Host</c> header, an IP address that isn't internal.
    /// </summary>
    private static bool TunnelsToPublicAddress(HttpRequestMessage tunnel)
    {
        var authority = tunnel.Headers.Host;
        if (authority is null || !Uri.TryCreate($"https://{authority}", UriKind.Absolute, out var target) ||
            !IPAddress.TryParse(target.Host.Trim('[', ']'), out var address))
        {
            return false;
        }

        return !(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).IsInternal();
    }

    /// <summary>
    /// Whether a connection's host is the one <paramref name="pin"/> was checked for: the name, or the address itself
    /// when the request names an address.
    /// </summary>
    private static bool Names(string host, SsrfPin pin)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).Equals(pin.Address);
        }

        return string.Equals(host, pin.Host, StringComparison.OrdinalIgnoreCase);
    }
}
