namespace Bitwarden.Server.Sdk.HealthChecks;

/// <summary>
/// Tags a health check registration can carry to say how a host treats its result.
/// </summary>
public static class HealthCheckTags
{
    /// <summary>
    /// The check describes something shared by every instance of the service, which taking one
    /// instance out of rotation would not fix: a backlog in a database they all drain, or messages
    /// set aside in a broker they all read. A host reports it, but leaves it out of its readiness probe.
    /// </summary>
    /// <remarks>
    /// Removing every instance from the load balancer over a shared backlog would turn a delay in
    /// the background into an outage in the foreground. Bitwarden.Server.Sdk.Service publishes these
    /// checks' status as the <c>bitwarden.health.status</c> metric and logs each change instead,
    /// which is where an alert on them belongs.
    /// </remarks>
    public const string Monitoring = "monitoring";
}
