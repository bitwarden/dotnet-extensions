using System.Security.Cryptography.X509Certificates;
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Trusts the authorities on every client's primary handler, after every other configuration of the client has run.
/// </summary>
internal sealed class TrustFilter(X509Certificate2Collection authorities) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);

        // A primary handler that hands requests on (a test's or a wrapper's) is followed to the one that sends them.
        var handler = builder.PrimaryHandler;
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        switch (handler)
        {
            case SocketsHttpHandler sockets:
                sockets.SslOptions.RemoteCertificateValidationCallback ??=
                    (_, certificate, chain, errors) => InternalTls.Trusts(authorities, certificate, chain, errors);
                break;
            case HttpClientHandler client:
                client.ServerCertificateCustomValidationCallback ??=
                    (_, certificate, chain, errors) => InternalTls.Trusts(authorities, certificate, chain, errors);
                break;
        }
    };
}
