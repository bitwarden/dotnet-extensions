using System.Diagnostics;
using Bitwarden.Server.Sdk.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring Bitwarden health checks.
/// </summary>
public static class HealthCheckServiceCollectionExtensions
{
    /// <summary>
    /// Adds Bitwarden health check services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <remarks>
    /// This method registers the <see cref="IHealthReporter"/> service which allows you to easily
    /// This method is safe to be called multiple times.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddBitwardenHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(sd => sd.ServiceType == typeof(InMemoryHealthReporter)))
        {
            // In order to allow access to the health reporter during the service construction phase
            // we use an implementation instance that is only created once so that we can grab it back
            // out of the service descriptor without having to build the services early.
            var reporter = new InMemoryHealthReporter();
            services.AddSingleton(reporter);
        }

        services.TryAddSingleton<IHealthReporter>(sp => sp.GetRequiredService<InMemoryHealthReporter>());

        services.TryAddSingleton<AdhocHealthCheck>();

        var healthChecksBuilder = services.AddHealthChecks();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<HealthCheckServiceOptions>, ConfigureHealthCheckServiceOptions>());

        return services;
    }

    /// <summary>
    ///
    /// </summary>
    /// <remarks>
    /// This method allows access to the health reporter during the service construction phase,
    /// before the service provider is built.
    /// </remarks>
    /// <param name="services"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static IHealthReporter GetHealthReporter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var healthReporterDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(InMemoryHealthReporter));

        if (healthReporterDescriptor == null)
        {
            throw new InvalidOperationException("You must call AddBitwardenHealthChecks before attempting to get a health reporter.");
        }

        Debug.Assert(healthReporterDescriptor.ImplementationInstance is IHealthReporter);
        return (IHealthReporter)healthReporterDescriptor.ImplementationInstance;
    }
}
