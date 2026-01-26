using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bitwarden.Server.Sdk.HealthChecks;

internal sealed class AdhocHealthCheck : IHealthCheck
{
    private readonly InMemoryHealthReporter _reporter;

    public AdhocHealthCheck(InMemoryHealthReporter reporter)
    {
        _reporter = reporter;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var allData = new Dictionary<string, object>();
        var healthStatus = HealthStatus.Healthy;

        foreach (var healthEvent in _reporter.Events)
        {
            if (healthEvent.Status == HealthStatus.Unhealthy)
            {
                healthStatus = HealthStatus.Unhealthy;
            }
            else if (healthStatus != HealthStatus.Unhealthy)
            {
                Debug.Assert(healthEvent.Status == HealthStatus.Degraded);
                healthStatus = HealthStatus.Degraded;
            }

            allData[healthEvent.IssueIdentifier] = healthEvent.Description;
        }

        return Task.FromResult(new HealthCheckResult(
            healthStatus,
            description: "Ad-hoc reported health information.",
            exception: null,
            data: allData
        ));
    }
}
