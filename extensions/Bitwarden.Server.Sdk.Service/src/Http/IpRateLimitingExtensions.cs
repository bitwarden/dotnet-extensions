using Microsoft.AspNetCore.Builder;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Adds the per-address request limits (<see cref="IpRateLimitOptions"/>) to the pipeline.
/// </summary>
internal static class IpRateLimitingExtensions
{
    public static IApplicationBuilder UseIpRateLimiting(this IApplicationBuilder app) =>
        app.UseMiddleware<IpRateLimitMiddleware>();
}
