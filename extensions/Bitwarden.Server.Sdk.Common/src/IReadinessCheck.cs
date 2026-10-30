namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Something a service cannot do its work without, asked whether it is there.
/// </summary>
/// <remarks>
/// <para>
/// The service's readiness probe asks every registered check and fails while any says no, which
/// takes the instance out of rotation without restarting it. That is the difference between
/// "degraded" and "down": a service missing one dependency keeps serving whatever does not need it,
/// and the orchestrator and the people watching it are told plainly what is missing.
/// </para>
/// <para>
/// Here rather than beside the probe, so that a package with no dependency on the web stack can
/// report into it.
/// </para>
/// </remarks>
public interface IReadinessCheck
{
    /// <summary>
    /// Whether the dependency is there, and if not, why.
    /// </summary>
    /// <remarks>
    /// Called on every probe, so it answers from state already known rather than by reaching out.
    /// </remarks>
    ReadinessResult Check();
}

/// <summary>
/// What a readiness check found.
/// </summary>
/// <param name="Ready">Whether the service can do this part of its work.</param>
/// <param name="Reason">
/// Why not, when it cannot. Shown to anybody who can reach the probe, which needs no credential, so
/// it names what is missing and never how the infrastructure behind it is reached.
/// </param>
public sealed record ReadinessResult(bool Ready, string? Reason = null)
{
    /// <summary>
    /// Ready.
    /// </summary>
    public static ReadinessResult Yes { get; } = new(true);

    /// <summary>
    /// Not ready, and why.
    /// </summary>
    public static ReadinessResult No(string reason) => new(false, reason);
}
