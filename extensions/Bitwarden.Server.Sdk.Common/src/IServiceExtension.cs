using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// A package's contribution to every service that references it.
/// </summary>
/// <remarks>
/// <para>
/// Lets a package configure a service without the service having to ask, so a required behavior
/// can't be forgotten. Declare it with <see cref="ServiceExtensionAttribute"/>.
/// </para>
/// <para>
/// Runs after the service's own registrations, so an extension can see what the service
/// registered, and a service that registers something first keeps it.
/// </para>
/// </remarks>
public interface IServiceExtension
{
    /// <summary>
    /// Adds this package's contribution to a service.
    /// </summary>
    /// <param name="services">The service's registrations, including its own.</param>
    /// <param name="configuration">The service's configuration.</param>
    void Configure(IServiceCollection services, IConfiguration configuration);
}
