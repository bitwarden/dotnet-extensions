using Bitwarden.Server.Sdk.Environment;
using Bitwarden.Server.Sdk.Service.Http;
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
    /// as an additional delegating handler and disabling automatic redirects on the primary handler.
    /// This ensures that all requests made by the client resolve DNS before connecting and block
    /// requests to internal/private IP ranges, including those reached via HTTP redirects.
    /// </summary>
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
            if (applies(services))
            {
                var logger = services.GetRequiredService<ILogger<SsrfProtectionHandler>>();
                handlers.Add(new SsrfProtectionHandler(logger) { FollowRedirects = followRedirects });
            }
        });

        // Disable auto-redirect on the primary handler so that redirects pass back through
        // SsrfProtectionHandler for validation on each hop. Without this, the inner handler
        // follows redirects internally, bypassing SSRF checks on redirect targets. A filter runs
        // after every configuration action, so a primary handler the client names after this call
        // is the one configured.
        builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
            new SsrfPrimaryHandlerFilter(builder.Name, applies));

        return builder;
    }
}
