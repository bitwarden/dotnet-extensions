using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bitwarden.Server.Sdk.HealthChecks;

internal sealed class InMemoryHealthReporter : IHealthReporter
{
    private readonly List<HealthEvent> _reportedIssues = [];
    internal IReadOnlyList<HealthEvent> Events => _reportedIssues;

    public IDisposable ReportDegradation(string issueIdentifier, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        var report = new HealthEvent(HealthStatus.Degraded, issueIdentifier, description);
        _reportedIssues.Add(report);
        return new Degradation(_reportedIssues, report);
    }

    public void ReportUnhealthy(string issueIdentifier, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        _reportedIssues.Add(new HealthEvent(HealthStatus.Unhealthy, issueIdentifier, description));
    }

    internal record HealthEvent(HealthStatus Status, string IssueIdentifier, string Description);

    private class Degradation(List<HealthEvent> issues, HealthEvent report) : IDisposable
    {
        public void Dispose()
        {
            issues.Remove(report);
        }
    }
}
