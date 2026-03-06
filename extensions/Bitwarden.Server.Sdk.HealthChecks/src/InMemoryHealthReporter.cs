using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bitwarden.Server.Sdk.HealthChecks;

internal sealed class InMemoryHealthReporter : IHealthReporter
{
    private readonly ConcurrentDictionary<Guid, HealthEvent> _reportedIssues = [];
    internal IEnumerable<HealthEvent> Events => _reportedIssues.Values;

    public IDisposable ReportDegradation(string issueIdentifier, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        var report = new HealthEvent(HealthStatus.Degraded, issueIdentifier, description);
        var id = Guid.NewGuid();
        _reportedIssues.TryAdd(id, report);
        return new Degradation(_reportedIssues, id);
    }

    public void ReportUnhealthy(string issueIdentifier, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issueIdentifier);

        _reportedIssues.TryAdd(Guid.NewGuid(), new HealthEvent(HealthStatus.Unhealthy, issueIdentifier, description));
    }

    internal record HealthEvent(HealthStatus Status, string IssueIdentifier, string Description);

    private class Degradation(ConcurrentDictionary<Guid, HealthEvent> issues, Guid id) : IDisposable
    {
        public void Dispose()
        {
            issues.Remove(id, out _);
        }
    }
}
