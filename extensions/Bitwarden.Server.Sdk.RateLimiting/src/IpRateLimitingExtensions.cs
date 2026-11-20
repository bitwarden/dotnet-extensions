using Microsoft.AspNetCore.Builder;

namespace Bitwarden.Server.Sdk.RateLimiting;

/// <summary>
/// Adds the per-address request limits (<see cref="IpRateLimitOptions"/>) to the pipeline.
/// </summary>
public static class IpRateLimitingExtensions
{
    /// <summary>
    /// Refuses a request over its address's limit, before anything else can answer it.
    /// </summary>
    /// <remarks>
    /// Add it after CORS, so a refusal carries the headers a browser needs to read it.
    /// </remarks>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseIpRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<IpRateLimitMiddleware>();
}
