using System.Net;
using System.Net.Sockets;

namespace Bitwarden.Server.Sdk.Http.Tests;

/// <summary>
/// A network where each name resolves to the addresses given for it and no other name resolves, and where every
/// connection, whatever it is for, reaches <paramref name="server"/>; keeping the names asked for and the endpoints
/// connected to.
/// </summary>
internal sealed class FakeNetwork(ScriptedServer? server, params (string Name, string[] Addresses)[] names) : SsrfConnector
{
    public List<string> Resolved { get; } = [];

    public List<EndPoint> Opened { get; } = [];

    public override Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        Resolved.Add(host);
        return names.FirstOrDefault(name => name.Name == host) is { Addresses: { } addresses }
            ? Task.FromResult(addresses.Select(IPAddress.Parse).ToArray())
            : throw new SocketException((int)SocketError.HostNotFound);
    }

    protected override async ValueTask<Stream> OpenAsync(EndPoint endpoint, CancellationToken cancellationToken)
    {
        Opened.Add(endpoint);
        if (server is null)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, server.Port, cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }

    /// <summary>
    /// A <see cref="SocketsHttpHandler"/> set up as <see cref="SsrfPrimaryHandlerFilter"/> sets one up, connecting
    /// through this network, through <paramref name="proxy"/> or else directly.
    /// </summary>
    public SocketsHttpHandler Sender(IWebProxy? proxy = null) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = proxy is not null,
        Proxy = proxy,
        ConnectCallback = ConnectAsync,
    };
}
