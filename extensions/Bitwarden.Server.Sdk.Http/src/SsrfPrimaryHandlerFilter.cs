using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Bitwarden.Server.Sdk.Http;

/// <summary>
/// Puts a guarded client's <see cref="SsrfProtectionHandler"/> below every handler the client adds, and sets its
/// primary handler up to connect only to the addresses the guard checked, after every other configuration of the
/// client has run.
/// </summary>
/// <remarks>
/// <para>
/// A configuration action reaches only the primary handler set when it runs, so one a client names after
/// <see cref="Microsoft.Extensions.DependencyInjection.HttpClientBuilderSsrfExtensions.AddSsrfProtection(Microsoft.Extensions.DependencyInjection.IHttpClientBuilder, bool)"/>
/// would keep its own settings. A filter wraps all of them.
/// </para>
/// <para>
/// The guard goes last among the additional handlers, so the client's own handlers (one attaching a credential, for
/// one) run once for each request the caller sends, above the guard, and never for a redirect the guard follows. Only
/// a handler a filter registered before this one adds afterwards, such as the factory's logging handler, sits below it.
/// </para>
/// <para>
/// The primary handler must be a <see cref="SocketsHttpHandler"/> with no
/// <see cref="SocketsHttpHandler.ConnectCallback"/> of its own. Its automatic redirects and cookies are turned off, its
/// proxy is fixed, and <see cref="SsrfConnector.ConnectAsync"/> opens its connections. Any other handler, a
/// <see cref="DelegatingHandler"/> wrapping one among them, fails the client's creation: it would see every redirect hop.
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

        // A primary handler that hands requests on (a wrapper) would sit below the guard and see every redirect hop.
        if (builder.PrimaryHandler is not SocketsHttpHandler sender)
        {
            throw new InvalidOperationException(
                $"SSRF protection on the '{builder.Name}' client needs a SocketsHttpHandler as its primary handler, and found {builder.PrimaryHandler.GetType().Name}. Configure a SocketsHttpHandler with the same settings instead, and add any other handler with AddHttpMessageHandler.");
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

        // The handler takes the process's default proxy when it first sends, and the guard decides each request by the
        // proxy: both use the one named here, so a default proxy changed later can't set them apart.
        if (sender.UseProxy)
        {
            sender.Proxy ??= HttpClient.DefaultProxy;
        }

        guard.Sender = sender;
    };
}
