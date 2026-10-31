namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// The outcome of a system probe.
/// </summary>
/// <param name="Status">
/// The service's status as a stable, machine-readable token, for example <c>HEALTHY</c>. Callers
/// are orchestrators rather than people, so this is an enumerated token and not prose.
/// </param>
/// <param name="Detail">
/// Why, when the status is not <c>HEALTHY</c>: what the service is missing, for whoever is looking
/// at the probe. Absent when healthy.
/// </param>
public sealed record SystemStatus(
    string Status,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Detail = null)
{
    /// <summary>
    /// The service is functioning normally.
    /// </summary>
    public static SystemStatus Healthy { get; } = new("HEALTHY");

    /// <summary>
    /// The service is running and cannot do all of its work, and why.
    /// </summary>
    public static SystemStatus Unhealthy(string detail) => new("UNHEALTHY", detail);
}
