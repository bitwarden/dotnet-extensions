namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Supplies the owner every restricted query is narrowed to.
/// </summary>
/// <remarks>
/// A context may legitimately have no organization, but a query against organization-owned rows may
/// not proceed without one. The <c>Require</c> methods are where "absent" becomes "refused".
/// </remarks>
public interface IRowOwnership
{
    /// <summary>
    /// The organization in scope.
    /// </summary>
    /// <exception cref="ForbiddenException">There is none.</exception>
    Guid RequireOrganization();

    /// <summary>
    /// The user in scope.
    /// </summary>
    /// <exception cref="ForbiddenException">There is none.</exception>
    Guid RequireUser();

    /// <summary>
    /// The provider in scope.
    /// </summary>
    /// <exception cref="ForbiddenException">There is none.</exception>
    Guid RequireProvider();

    /// <summary>
    /// The organization in scope, or null when the request names none.
    /// </summary>
    /// <remarks>
    /// For rows owned by either a person or an organization, where an absent owner is an ordinary
    /// state rather than a refusal.
    /// </remarks>
    Guid? Organization();

    /// <summary>
    /// The user in scope, or null when the request names none.
    /// </summary>
    Guid? User();

    /// <summary>
    /// The provider in scope, or null when the request names none.
    /// </summary>
    Guid? Provider();
}
