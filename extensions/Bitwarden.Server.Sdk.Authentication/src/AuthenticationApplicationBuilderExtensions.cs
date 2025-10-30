using Bitwarden.Server.Sdk.Authentication;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods to add Bitwarden-style authentication to the HTTP application pipeline.
/// </summary>
public static class AuthenticationApplicationBuilderExtensions
{
    /// <summary>
    /// Uses Bitwarden-style Authentication middleware.
    /// This will always call <see cref="AuthAppBuilderExtensions.UseAuthentication"/> and all rules for that function
    /// apply to this one. It should be called after <c>UseRouting</c> and before <c>UseAuthorization</c>.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to add the middleware to.</param>
    /// <returns>A reference to this instance after the operation has completed.</returns>
    public static IApplicationBuilder UseBitwardenAuthentication(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();

        app.UseMiddleware<PostAuthenticationLoggingMiddleware>();

        return app;
    }
}
