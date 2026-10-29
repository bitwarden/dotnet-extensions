namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Says whether the request being handled may name an organization on an outgoing call as the
/// organization it has just created (the Client package's <c>OutgoingCall.AboutCreatedOrganization</c>).
/// </summary>
/// <remarks>
/// Implemented by the hosting package, which knows the routed operation: only an operation declared
/// to create an organization, naming none itself, and only one organization per request. With no
/// implementation registered nothing is allowed.
/// </remarks>
public interface IOrganizationCreation
{
    /// <summary>Whether <paramref name="organization"/> may be named as the one this request created.</summary>
    /// <param name="organization">The organization the call is about.</param>
    bool Allows(Guid organization);
}
