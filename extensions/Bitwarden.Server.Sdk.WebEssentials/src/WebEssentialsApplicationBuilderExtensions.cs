using Bitwarden.Server.Sdk.WebEssentials;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for <see cref="IApplicationBuilder"/> provided by Bitwarden Web Essentials.
/// </summary>
public static class WebEssentialsApplicationBuilderExtensions
{
    /// <summary>
    /// Adds middleware that appends common security headers to every HTTP response.
    /// </summary>
    /// <remarks>
    /// This middleware should be registered in any project that exposes HTTP endpoints consumed by Bitwarden clients.
    /// The following headers are added to all responses regardless of content type, including <c>application/json</c>:
    /// <list type="bullet">
    ///   <item><description><c>x-frame-options: SAMEORIGIN</c> — prevents the page from being embedded in a frame on a different origin.</description></item>
    ///   <item><description><c>x-xss-protection: 1; mode=block</c> — instructs legacy browsers to block pages when a reflected XSS attack is detected.</description></item>
    ///   <item><description><c>x-content-type-options: nosniff</c> — prevents browsers from MIME-sniffing a response away from the declared content type.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to add the middleware to.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> so that calls can be chained.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<SecurityHeadersMiddleware>();

        return app;
    }
}
