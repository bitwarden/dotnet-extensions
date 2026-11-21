using System.Net;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// One guarded <see cref="SocketsHttpHandler"/>'s <see cref="SocketsHttpHandler.ConnectCallback"/>: connects through
/// <paramref name="network"/>, knowing the handler's proxy, so a tunnel is opened only to that proxy.
/// </summary>
/// <param name="network">The network the guard checks against.</param>
/// <param name="proxy">The handler's proxy, fixed by <see cref="SsrfPrimaryHandlerFilter"/>; null when it uses none.</param>
internal sealed class SsrfConnection(SsrfConnector network, IWebProxy? proxy)
{
    public ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        network.ConnectAsync(context, proxy, cancellationToken);
}
