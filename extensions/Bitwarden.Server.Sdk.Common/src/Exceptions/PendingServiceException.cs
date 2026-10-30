namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// A rule this service cannot enforce yet, because the data it turns on belongs to a service
/// or provider that is not available yet.
/// </summary>
/// <remarks>
/// <para>
/// A rule that can't be enforced must not be silently dropped. The operation refuses with a
/// <c>501</c> naming the service it is waiting on and what it needs, so callers can't mistake it
/// for working, and searching for this type lists everything a service is waiting for.
/// </para>
/// <para>
/// Derives from <see cref="NotImplementedException"/>, so it is answered like one.
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
    /// <summary>
    /// The service being waited on.
    /// </summary>
    public string Service { get; } = service;

    /// <summary>
    /// What is needed of it.
    /// </summary>
    public string Requirement { get; } = requirement;
}
