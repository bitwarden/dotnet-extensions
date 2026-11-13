using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// Puts a guarded client's <see cref="SsrfProtectionHandler"/> directly above its primary handler, and sets that
/// handler up to connect only to the addresses the guard checked, after every other configuration of the client has
/// run.
/// </summary>
/// <remarks>
/// <para>
/// A configuration action reaches only the primary handler set when it runs, so one a client names after
/// <see cref="Microsoft.Extensions.DependencyInjection.HttpClientBuilderSsrfExtensions.AddSsrfProtection(Microsoft.Extensions.DependencyInjection.IHttpClientBuilder, bool)"/>
/// would keep its own settings. A filter wraps all of them.
/// </para>
/// <para>
/// The guard goes last among the additional handlers, so the client's own handlers (one attaching a credential, for
/// one) run once for each request the caller sends, above the guard, and never for a redirect the guard follows.
/// </para>
/// <para>
/// The primary handler, or the handler a <see cref="DelegatingHandler"/> primary hands requests on to, must be a
/// <see cref="SocketsHttpHandler"/> with no <see cref="SocketsHttpHandler.ConnectCallback"/> of its own. Its automatic
/// redirects and cookies are turned off, and <see cref="SsrfConnector.ConnectAsync"/> opens its connections. The
/// factory's own primary handler, an <see cref="HttpClientHandler"/> with nothing set, is replaced by a new
/// <see cref="SocketsHttpHandler"/>; any other handler fails the client's creation.
/// </para>
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

        // A client whose handlers were cleared afterwards (a test answering in process, for one) has no guard to set
        // up, and is left as it is.
        if (builder.AdditionalHandlers.OfType<SsrfProtectionHandler>().FirstOrDefault() is not { } guard)
        {
            return;
        }

        builder.AdditionalHandlers.Remove(guard);
        builder.AdditionalHandlers.Add(guard);

        // The factory's own primary handler, an HttpClientHandler with nothing set, can't pin connections: a
        // SocketsHttpHandler takes its place. Settings the guard sets itself don't count as something set.
        if (builder.PrimaryHandler is HttpClientHandler unset && HasNothingSet(unset))
        {
            builder.PrimaryHandler = new SocketsHttpHandler();
            unset.Dispose();
        }

        // A primary handler that hands requests on (a wrapper's) is followed to the one that sends them.
        var handler = builder.PrimaryHandler;
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        if (handler is not SocketsHttpHandler sender)
        {
            throw new InvalidOperationException(
                $"SSRF protection on the '{builder.Name}' client needs a SocketsHttpHandler to send its requests, and found {handler.GetType().Name}. Configure a SocketsHttpHandler with the same settings instead.");
        }

        var connector = builder.Services.GetRequiredService<SsrfConnector>();
        Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> connect = connector.ConnectAsync;
        if (sender.ConnectCallback is not null && sender.ConnectCallback != connect)
        {
            throw new InvalidOperationException(
                $"SSRF protection on the '{builder.Name}' client connects to the addresses it checked itself, and its SocketsHttpHandler already has a ConnectCallback.");
        }

        sender.AllowAutoRedirect = false;
        sender.UseCookies = false;
        sender.ConnectCallback = connect;
        guard.Sender = sender;
    };

    /// <summary>
    /// Whether <paramref name="handler"/> has every setting as a new one has it, but those the guard sets itself:
    /// redirects and cookies.
    /// </summary>
    private static bool HasNothingSet(HttpClientHandler handler)
    {
        using var fresh = new HttpClientHandler();
        return handler.AutomaticDecompression == fresh.AutomaticDecompression &&
            handler.CheckCertificateRevocationList == fresh.CheckCertificateRevocationList &&
            handler.ClientCertificateOptions == fresh.ClientCertificateOptions &&
            (handler.ClientCertificateOptions != ClientCertificateOption.Manual || handler.ClientCertificates.Count == 0) &&
            handler.Credentials is null &&
            handler.DefaultProxyCredentials is null &&
            handler.MaxConnectionsPerServer == fresh.MaxConnectionsPerServer &&
            handler.MaxRequestContentBufferSize == fresh.MaxRequestContentBufferSize &&
            handler.MaxResponseHeadersLength == fresh.MaxResponseHeadersLength &&
            handler.PreAuthenticate == fresh.PreAuthenticate &&
            handler.Proxy is null &&
            handler.ServerCertificateCustomValidationCallback is null &&
            handler.SslProtocols == fresh.SslProtocols &&
            handler.UseDefaultCredentials == fresh.UseDefaultCredentials &&
            handler.UseProxy == fresh.UseProxy &&
            handler.Properties.Count == 0;
    }
}
