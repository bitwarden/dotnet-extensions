namespace Bitwarden.Server.Sdk.HealthChecks;

/// <summary>
/// Provides methods to report health status changes for health checks.
/// </summary>
public interface IHealthReporter
{
    /// <summary>
    /// Reports a temporary degradation in health for the current service. The degradation is automatically
    /// cleared when the returned <see cref="IDisposable"/> is disposed.
    /// </summary>
    /// <remarks>
    /// This method is intended for temporary, recoverable issues. Multiple degradations can be active
    /// simultaneously, and health checks will report degraded status as long as at least one degradation
    /// is active.
    /// </remarks>
    /// <param name="issueIdentifier">A machine readable string that can be used to identify the current issue.</param>
    /// <param name="description">A human readable description of the issue being experienced, this value should only
    /// contain information that is safe to be logged.
    /// </param>
    /// <returns>
    /// An <see cref="IDisposable"/> that clears the degradation when disposed.
    /// Use in a <c>using</c> statement to automatically restore health status.
    /// </returns>
    IDisposable ReportDegradation(string issueIdentifier, string description);

    /// <summary>
    /// Reports an unhealthy status for the current service. Unlike degradation, this status persists
    /// until the application restarts or the issue is resolved.
    /// </summary>
    /// <remarks>
    /// This method should be used for critical, non-recoverable issues that require intervention.
    /// Once marked unhealthy, the status cannot be automatically cleared and typically requires
    /// application restart or manual resolution.
    /// </remarks>
    /// <param name="issueIdentifier">A machine readable string that can be used to identify the current issue.</param>
    /// <param name="description">A human readable description of the issue being experienced, this value should only
    /// contain information that is safe to be logged.
    /// </param>
    void ReportUnhealthy(string issueIdentifier, string description);
}
