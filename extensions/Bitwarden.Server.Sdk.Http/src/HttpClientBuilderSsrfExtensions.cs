using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering SSRF protection on named HTTP clients.
/// </summary>
/// <remarks>
/// A service sends a request whose destination someone outside Bitwarden chose (an organization's identity provider,
/// a webhook) through this guard.
/// </remarks>
public static class HttpClientBuilderSsrfExtensions
{
    /// <summary>
    /// Adds SSRF protection to an HTTP client by registering the <see cref="SsrfProtectionHandler"/>
    /// as an additional delegating handler, directly above the primary handler, and connecting only to the addresses
    /// it checked. This ensures that all requests made by the client resolve DNS before connecting and block
    /// requests to internal/private IP ranges, including those reached via HTTP redirects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's primary handler must be a <see cref="SocketsHttpHandler"/> (the default) with no
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> of its own; creating the client fails otherwise. Its automatic
    /// redirects and cookies are turned off. The guard runs below every other handler the client adds, so those run
    /// once for each request the caller sends and never for a redirect the guard follows.
    /// </para>
    /// <para>
    /// A client guarded more than once, by <see cref="HttpClientFactoryServiceCollectionExtensions.ConfigureHttpClientDefaults"/>
    /// and by its own registration for one, gets one guard, from the registration that applies first, with that
    /// registration's <paramref name="followRedirects"/>. Every client's defaults apply before its own registrations.
    /// </para>
    /// </remarks>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/> to configure.</param>
    /// <param name="followRedirects">
    /// When <c>true</c> (default), the handler follows HTTP redirects and validates each hop against
    /// SSRF rules. When <c>false</c>, the handler validates the initial request only and returns
    /// redirect responses as-is to the caller. Use <c>false</c> for clients that implement their
    /// own redirect-following logic.
    /// </param>
    /// <returns>The <see cref="IHttpClientBuilder"/> so that additional calls can be chained.</returns>
    public static IHttpClientBuilder AddSsrfProtection(this IHttpClientBuilder builder, bool followRedirects = true) =>
        builder.AddSsrfProtection(_ => true, followRedirects);

    /// <summary>
    /// <see cref="AddSsrfProtection(IHttpClientBuilder, bool)"/> in Bitwarden's cloud only: a self-hosted install's
    /// client is left as it was, following redirects itself.
    /// </summary>
    /// <remarks>
    /// Suits a client such as an OpenID Connect back-channel: a self-hosted install's identity provider often runs on
    /// the same private network, and the guard has no allowlist.
    /// Read from <see cref="IBitwardenEnvironment.SelfHosted"/> when the client's handlers are built.
    /// </remarks>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/> to configure.</param>
    /// <param name="followRedirects">As <see cref="AddSsrfProtection(IHttpClientBuilder, bool)"/>.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> so that additional calls can be chained.</returns>
    public static IHttpClientBuilder AddCloudSsrfProtection(this IHttpClientBuilder builder, bool followRedirects = true) =>
        builder.AddSsrfProtection(services => !services.GetRequiredService<IBitwardenEnvironment>().SelfHosted, followRedirects);

    private static IHttpClientBuilder AddSsrfProtection(
        this IHttpClientBuilder builder, Func<IServiceProvider, bool> applies, bool followRedirects)
    {
        builder.ConfigureAdditionalHttpMessageHandlers((handlers, services) =>
        {
            // A client guarded twice (by every client's defaults and by its own registration, say) keeps one guard: a
            // second, below the first, would follow a redirect itself, and the first would refuse its answer.
            if (applies(services) && !handlers.OfType<SsrfProtectionHandler>().Any())
            {
                var logger = services.GetRequiredService<ILogger<SsrfProtectionHandler>>();
                handlers.Add(new SsrfProtectionHandler(logger, services.GetRequiredService<SsrfConnector>())
                {
                    FollowRedirects = followRedirects,
                });
            }
        });

        // Put the guard directly above the primary handler, disable auto-redirect and cookies on the primary handler,
        // and have it connect to the addresses the guard checked. Without this, the inner handler follows redirects
        // internally, bypassing SSRF checks on redirect targets, and connects wherever the name resolves by then. A
        // filter runs after every configuration action, so a primary handler the client names after this call is the
        // one configured.
        builder.Services.TryAddSingleton<SsrfConnector>();
        builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
            new SsrfPrimaryHandlerFilter(builder.Name, applies));

        return builder;
    }
}
