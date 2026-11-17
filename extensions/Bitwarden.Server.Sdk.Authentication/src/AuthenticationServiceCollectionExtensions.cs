using Bitwarden.Server.Sdk.Authentication;
using Microsoft.AspNetCore.Authentication;
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
    /// <remarks>
    /// Data protection is not registered since bearer token validation does not need it. Applications that add schemes
    /// that do need it, such as cookies, should do so through <c>AddAuthentication</c> which will register it.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for additional chaining.</returns>
    public static IServiceCollection AddBitwardenAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AddAuthentication would also register data protection, only take the parts the JWT bearer handler needs.
        services.AddAuthenticationCore(options => options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme);
        services.AddWebEncoders();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAuthenticationConfigurationProvider, BitwardenAuthenticationConfigurationProvider>();

        new AuthenticationBuilder(services)
            .AddJwtBearer();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, BitwardenConfigureJwtBearerOptions>());

        return services;
    }
}
