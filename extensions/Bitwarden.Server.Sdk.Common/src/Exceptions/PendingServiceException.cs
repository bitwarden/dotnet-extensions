namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// A rule this service cannot enforce yet, because the data it turns on belongs to a service
/// or provider that is not available yet.
/// </summary>
/// <remarks>
/// <para>
/// A service is sometimes built before everything it depends on: a rule whose inputs live in
/// another service, or with an outside provider, that does not exist yet. There are three things
/// to do with such a rule: enforce it against a dependency that is not there, drop it, or say so. The first is impossible and the second is silent — a rule quietly
/// missing from a service is indistinguishable from a rule nobody ever knew about, and it will
/// be found by whoever it fails to protect.
/// </para>
/// <para>
/// So it is said out loud, and loudly: the operation refuses with a <c>501</c> naming the
/// service it is waiting on and what it needs from it. Callers cannot mistake it for working,
/// and <c>PendingServiceException</c> greps to a complete list of what this service is waiting
/// for — its dependency order, written down as it is discovered rather than reconstructed later.
/// </para>
/// <para>
/// Derives from <see cref="NotImplementedException"/> so the platform's existing mapping
/// answers it, and so nothing has to special-case it to behave correctly.
/// </para>
/// </remarks>
/// <param name="service">The service being waited on, as it will be named.</param>
/// <param name="requirement">What is needed of it, as a verb phrase.</param>
public sealed class PendingServiceException(string service, string requirement)
    : NotImplementedException(
        $"This needs the {service} service to {requirement}. That service does not exist yet, so "
        + "the rule cannot be enforced and the request is refused rather than allowed through "
        + "unchecked.")
{
    /// <summary>The service being waited on.</summary>
    public string Service { get; } = service;

    /// <summary>What is needed of it.</summary>
    public string Requirement { get; } = requirement;
}
