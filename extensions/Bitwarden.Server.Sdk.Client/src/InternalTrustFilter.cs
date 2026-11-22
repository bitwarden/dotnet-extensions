using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Gives each client named in <see cref="InternalTrustedClients"/> the internal authorities' chain policy, on the
/// handler it sends with once every other configuration of it has run.
/// </summary>
/// <remarks>
/// A filter rather than a primary handler of its own, so the trust doesn't depend on order: a primary handler set
/// for every client (<c>ConfigureHttpClientDefaults</c>), or set for this one before or after
/// <c>TrustInternalAuthorities</c>, is kept, and is the one trusted. A primary handler that hands requests on (a
/// wrapper's) is followed to the one that sends them.
/// </remarks>
internal sealed class InternalTrustFilter(IOptions<InternalAuthorities> authorities, IOptions<InternalTrustedClients> clients)
    : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);

        if (builder.Name is not { } name || !clients.Value.Names.Contains(name))
        {
            return;
        }

        var certificates = authorities.Value.Certificates;
        if (certificates.Count == 0)
        {
            return;
        }

        var handler = builder.PrimaryHandler;
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        // Only a SocketsHttpHandler takes a chain policy. Any other would trust the system's roots instead, so it is
        // refused rather than left to call our own services with the wrong trust.
        if (handler is not SocketsHttpHandler sockets)
        {
            throw new InvalidOperationException(
                $"The '{name}' client calls the deployment's own services, so it trusts {InternalTls.Section}:CertificateAuthorities, "
                + $"which needs a SocketsHttpHandler; it sends with {handler.GetType().Name}.");
        }

        // A policy the client sets itself is kept.
        sockets.SslOptions.CertificateChainPolicy ??= InternalTls.ChainPolicy(certificates);
    };
}
