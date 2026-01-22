using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.HealthChecks;

internal sealed class ConfigureHealthCheckServiceOptions : IConfigureOptions<HealthCheckServiceOptions>
{
    public void Configure(HealthCheckServiceOptions options)
    {
        options.Registrations.Add(new HealthCheckRegistration(
            name: "Ad-hoc",
            factory: (sp) => sp.GetRequiredService<AdhocHealthCheck>(),
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ad-hoc"]
        ));
    }
}
