using Bitwarden.Server.Sdk.WebEssentials;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring Bitwarden Web Essentials services.
/// </summary>
public static class WebEssentialsServiceCollectionExtensions
{
    /// <summary>
    /// Adds services required by Bitwarden Web Essentials, including JSON serialization metadata
    /// for types used by endpoints registered via <see cref="Microsoft.AspNetCore.Routing.WebEssentialsEndpointRouteBuilderExtensions.MapVersionEndpoint(AspNetCore.Routing.IEndpointRouteBuilder)"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddWebEssentials(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Add(WebEssentialsJsonSerializerContext.Default);
        });

        return services;
    }
}
