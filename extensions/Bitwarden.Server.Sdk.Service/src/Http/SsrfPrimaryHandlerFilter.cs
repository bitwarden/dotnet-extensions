using Microsoft.Extensions.Http;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Turns automatic redirects off on a guarded client's primary handler, after every other configuration of the
/// client has run.
/// </summary>
/// <remarks>
/// A configuration action reaches only the primary handler set when it runs, so one a client names after
/// <see cref="Microsoft.Extensions.DependencyInjection.HttpClientBuilderSsrfExtensions.AddSsrfProtection(Microsoft.Extensions.DependencyInjection.IHttpClientBuilder, bool)"/>
/// would keep following redirects below the guard. A filter wraps all of them.
/// </remarks>
/// <param name="name">The guarded client's name; null when every client is guarded.</param>
/// <param name="applies">Whether the guard applies, read from the client's services.</param>
internal sealed class SsrfPrimaryHandlerFilter(string? name, Func<IServiceProvider, bool> applies) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);

        if (name is not null && builder.Name != name)
        {
            return;
        }

        if (!applies(builder.Services))
        {
            return;
        }

        // A primary handler that hands requests on (a test's or a wrapper's) is followed to the one that sends them.
        var handler = builder.PrimaryHandler;
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        switch (handler)
        {
            case SocketsHttpHandler socketsHttpHandler:
                socketsHttpHandler.AllowAutoRedirect = false;
                break;
            case HttpClientHandler httpClientHandler:
                httpClientHandler.AllowAutoRedirect = false;
                break;
            default:
                // Any other handler (a test's fake, for one) has no switch to turn off. It is still guarded: an
                // answer it gives for an address the guard did not send to is refused, not returned.
                break;
        }
    };
}
