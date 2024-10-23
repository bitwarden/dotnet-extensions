using Bitwarden.Extensions.Hosting.Features;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Feature extension methods for <see cref="IApplicationBuilder"/>.
/// </summary>
public static class FeatureApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the <see cref="FeatureCheckMiddleware"/> to the specified <see cref="IApplicationBuilder"/>, which enabled feature check capabilities.
    /// <para>
    /// This call must take place between <c>app.UseRouting()</c> and <c>app.UseEndpoints(...)</c> for middleware to function properly.
    /// </para>
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> to add the middleware to.</param>
    /// <returns>A reference to <paramref name="app"/> after the operation has completed.</returns>
    public static IApplicationBuilder UseFeatureFlagChecks(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // This would be a good time to make sure that IFeatureService is registered but it is a scoped service
        // and I don't think creating a scope is worth it for that. If we think this is a problem we can add another
        // marker interface that is registered as a singleton and validate that it exists here.

        app.UseMiddleware<FeatureCheckMiddleware>();
        return app;
    }
}
