namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// A request body naming the organization its action is about (<see cref="NamesOrganizationAttribute"/>).
/// </summary>
public interface INamesOrganization
{
    /// <summary>
    /// The organization the action is about.
    /// </summary>
    Guid OrganizationId { get; }
}
