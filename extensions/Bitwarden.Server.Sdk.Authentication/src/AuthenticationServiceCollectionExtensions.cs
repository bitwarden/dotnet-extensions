using Bitwarden.Server.Sdk.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for setting up Bitwarden-style authentication in a <see cref="IServiceCollection"/>.
/// </summary>
public static class BitwardenAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Adds Bitwarden compatible authentication, can be configured through the `Authentication:Schemes:Bearer` config
    /// section.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for additional chaining.</returns>
    public static IServiceCollection AddBitwardenAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, BitwardenConfigureJwtBearerOptions>());

        return services;
    }
}
