using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for declaring the infrastructure a service needs.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Declares that the service needs infrastructure of the given <paramref name="kind"/>,
    /// described by <paramref name="properties"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declarations are only evaluated when infrastructure is being collected, for example by
    /// <c>Bitwarden.Server.Sdk.Infrastructure.Testing</c>, and have no effect on how the service runs.
    /// </para>
    /// <para>
    /// Declaring the same kind with deeply equal properties more than once results in a single
    /// requirement. When a requirement depends on other services, configure
    /// <see cref="InfrastructureOptions"/> directly with the options pattern instead.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to declare the requirement against.</param>
    /// <param name="kind">The kind of infrastructure, for example <c>database</c>.</param>
    /// <param name="properties">
    /// Properties that describe the requirement, for example its name. The object is copied each
    /// time requirements are collected, so it should not be changed after this call.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddInfrastructureRequirement(this IServiceCollection services, string kind, JsonObject? properties = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(kind);

        services.Configure<InfrastructureOptions>(options => options.Add(kind, properties));

        return services;
    }
}
