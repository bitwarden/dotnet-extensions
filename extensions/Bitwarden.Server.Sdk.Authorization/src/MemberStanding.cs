namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// A confirmed member's standing in an organization.
/// </summary>
/// <param name="Role">Their role.</param>
/// <param name="Permissions">
/// The permissions granted to a <see cref="OrganizationRole.Custom"/> member; empty for every other role, which holds a
/// permission by the role itself or not at all.
/// </param>
public sealed record MemberStanding(OrganizationRole Role, IReadOnlySet<OrganizationPermission> Permissions)
{
    /// <summary>
    /// A member in <paramref name="role"/>, granted no permission of their own.
    /// </summary>
    /// <param name="role">Their role.</param>
    /// <returns>The standing.</returns>
    public static MemberStanding Of(OrganizationRole role) => new(role, new HashSet<OrganizationPermission>());
}
