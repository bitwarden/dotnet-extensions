using Bitwarden.Server.Sdk.Environment;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> provided by Bitwarden Web Essentials.
/// </summary>
public static class WebEssentialsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a <c>GET /version</c> endpoint that returns the informational version of the application assembly.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to map the route on.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> that can be used to further customize the endpoint.</returns>
    public static IEndpointConventionBuilder MapVersionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var environment = endpoints.ServiceProvider.GetRequiredService<IBitwardenEnvironment>();
        var version = string.IsNullOrEmpty(environment.Version) ? null : environment.Version;

        return endpoints.MapGet("/version", () =>
        {
            return TypedResults.Ok(version);
        });
    }
}
