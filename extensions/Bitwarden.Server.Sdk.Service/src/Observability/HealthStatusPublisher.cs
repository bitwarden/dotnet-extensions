using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Bitwarden.Server.Sdk.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.Observability;

/// <summary>
/// Makes the checks the readiness probe leaves out visible: a gauge of each one's status, and a
/// log line whenever it changes.
/// </summary>
/// <remarks>
/// <para>
/// A check tagged <see cref="HealthCheckTags.Monitoring"/> never fails readiness, and a degraded
/// result never does either, so without this nobody would ever see them. ASP.NET Core runs every
/// check on its publisher's schedule (5 seconds after start, then every 30); this keeps the
/// monitoring ones.
/// </para>
/// <para>
/// <c>bitwarden.health.status</c> is 0 for healthy, 1 for degraded and 2 for unhealthy, by check
/// (<c>bitwarden.health.check</c>), which is what an alert is set on. The log line carries the
/// check's description, which by the checks' own rule holds identifiers and counts only.
/// </para>
/// </remarks>
internal sealed class HealthStatusPublisher : IHealthCheckPublisher
{
    /// <summary>
    /// The meter the status gauge is on.
    /// </summary>
    internal const string MeterName = "Bitwarden.Server.Sdk.Service";

    private readonly ConcurrentDictionary<string, HealthStatus> _statuses = new(StringComparer.Ordinal);
    private readonly ILogger<HealthStatusPublisher> _logger;

    public HealthStatusPublisher(IMeterFactory meters, ILogger<HealthStatusPublisher> logger)
    {
        _logger = logger;

        meters.Create(MeterName).CreateObservableGauge(
            "bitwarden.health.status",
            Observe,
            unit: "{status}",
            description: "Each monitoring health check's last status: 0 healthy, 1 degraded, 2 unhealthy.");
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        foreach (var (name, entry) in report.Entries)
        {
            if (!entry.Tags.Contains(HealthCheckTags.Monitoring))
            {
                continue;
            }

            var previous = _statuses.TryGetValue(name, out var known) ? known : HealthStatus.Healthy;
            _statuses[name] = entry.Status;

            if (previous == entry.Status)
            {
                continue;
            }

            var level = entry.Status switch
            {
                HealthStatus.Unhealthy => LogLevel.Error,
                HealthStatus.Degraded => LogLevel.Warning,
                _ => LogLevel.Information,
            };

            // The description only: an exception's text is for the check's own logging.
            _logger.Log(
                level,
                "Health check {Check} is {Status} (was {Previous}): {Description}",
                name,
                entry.Status,
                previous,
                entry.Description ?? (entry.Exception is null ? "no description" : entry.Exception.GetType().Name));
        }

        return Task.CompletedTask;
    }

    private IEnumerable<Measurement<int>> Observe()
    {
        foreach (var (name, status) in _statuses)
        {
            var value = status switch
            {
                HealthStatus.Unhealthy => 2,
                HealthStatus.Degraded => 1,
                _ => 0,
            };

            yield return new Measurement<int>(value, new KeyValuePair<string, object?>("bitwarden.health.check", name));
        }
    }
}
