using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The probe endpoints every Bitwarden service exposes, modelled on Kubernetes' three probe kinds.
/// </summary>
/// <remarks>
/// Kubernetes distinguishes the three because it reacts to each differently: a failed liveness probe
/// restarts the container, a failed readiness probe removes it from the load balancer without
/// restarting it, and a failed startup probe suppresses the other two until the service has
/// finished coming up. Collapsing them into one endpoint loses those distinctions, so all three are
/// exposed separately even while they share an implementation.
/// </remarks>
internal static class SystemEndpoints
{
    private const string RoutePrefix = "/api/v1";

    /// <summary>
    /// The section the probes are published under.
    /// </summary>
    internal const string Tag = "System";

    /// <summary>
    /// What the section is, for the published document.
    /// </summary>
    internal const string TagDescription =
        "The probes every Bitwarden service answers, for the infrastructure that runs it: whether the "
        + "process is alive, ready for traffic, and finished starting. Anonymous, and they carry no "
        + "Vault Data or account information.";

    /// <summary>
    /// Maps the probe endpoints under <c>/api/v1</c>, tagged <c>System</c>.
    /// </summary>
    public static void MapSystemEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Probes are consumed by infrastructure that has no credential and no organization.
        var group = endpoints.MapGroup(RoutePrefix).WithTags(Tag).AllowAnonymous();

        group.MapGet("health", Health).WithName("System_Health");
        group.MapGet("readiness", Readiness).WithName("System_Readiness");
        group.MapGet("startup", Startup).WithName("System_Startup");
    }

    /// <summary>
    /// Liveness: reports whether the process is running and able to serve.
    /// </summary>
    /// <remarks>
    /// An orchestrator restarts the container when this fails, so it must not depend on anything a
    /// restart would not fix. A failing database is a readiness concern, not a liveness one.
    /// </remarks>
    /// <returns>Always <c>HEALTHY</c>: nothing is evaluated beyond the process answering.</returns>
    public static Ok<SystemStatus> Health() => TypedResults.Ok(SystemStatus.Healthy);

    /// <summary>
    /// Readiness: reports whether the service can currently accept traffic.
    /// </summary>
    /// <remarks>
    /// An orchestrator takes the instance out of rotation when this fails, without restarting it.
    /// Every registered health check is asked, and any that is unhealthy makes this a <c>503</c>
    /// naming what is missing. A degraded check still serves. A check that throws is reported
    /// without its exception text, because the probe needs no credential.
    /// </remarks>
    /// <param name="health">The service's health checks.</param>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns><c>HEALTHY</c>, or <c>UNHEALTHY</c> with what is missing.</returns>
    public static async Task<Results<Ok<SystemStatus>, JsonHttpResult<SystemStatus>>> Readiness(
        [Microsoft.AspNetCore.Mvc.FromServices] HealthCheckService health,
        CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(cancellationToken);

        var missing = report.Entries.Values
            .Where(entry => entry.Status == HealthStatus.Unhealthy)
            .Select(entry => entry.Exception is null && !string.IsNullOrEmpty(entry.Description)
                ? entry.Description
                : "A dependency is not ready.")
            .ToList();

        return missing.Count == 0
            ? TypedResults.Ok(SystemStatus.Healthy)
            : TypedResults.Json(
                SystemStatus.Unhealthy(string.Join(" ", missing)),
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Startup: reports whether the service has finished initialising.
    /// </summary>
    /// <remarks>
    /// While this fails, an orchestrator suppresses the liveness and readiness probes, which stops a
    /// slow start from being mistaken for a fault and restarted in a loop.
    /// </remarks>
    /// <returns>Always <c>HEALTHY</c>: nothing is evaluated beyond the service answering.</returns>
    public static Ok<SystemStatus> Startup() => TypedResults.Ok(SystemStatus.Healthy);
}
