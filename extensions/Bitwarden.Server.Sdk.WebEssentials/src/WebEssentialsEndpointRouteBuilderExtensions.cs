using System.Reflection;
using Bitwarden.Server.Sdk.WebEssentials;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        var appName = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>().ApplicationName;
        var version = ParseVersion(Assembly.Load(appName)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion);

        return endpoints.MapGet("/version", () =>
        {
            return TypedResults.Ok(new VersionResponse(version));
        });
    }

    private static string? ParseVersion(string? rawVersion)
    {
        var plusIdx = rawVersion?.IndexOf('+') ?? -1;
        return plusIdx >= 0 ? rawVersion![..plusIdx] : rawVersion;
    }
}
