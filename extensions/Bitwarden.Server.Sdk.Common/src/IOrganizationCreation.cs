namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Says whether the request being handled may name, on an outgoing call, an organization it has
/// just created.
/// </summary>
/// <remarks>
/// Implemented by the hosting package, which knows the routed operation. With no implementation
/// registered, nothing is allowed.
/// </remarks>
public interface IOrganizationCreation
{
    /// <summary>
    /// Whether <paramref name="organization"/> may be named as the one this request created.
    /// </summary>
    /// <param name="organization">The organization the call is about.</param>
    bool Allows(Guid organization);
}
