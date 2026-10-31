namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// What a caller may do in the organization their session is scoped to.
/// </summary>
/// <remarks>
/// <para>
/// Ordered by reach, so a comparison answers "at least an administrator" without a lookup
/// table, and <c>OrganizationRole = User</c> reads as "must be a member at all".
/// </para>
/// <para>
/// <see cref="Custom"/> sits above <see cref="User"/> because a custom member is a member, and
/// below <see cref="Admin"/> because how much further they reach is a question about their
/// permissions rather than their role. An operation that wants one of those asks for the
/// permission, not for the role.
/// </para>
/// <para>
/// The values are the platform's own and are ordered for comparison. They deliberately do not
/// follow the numbering of a member's stored type, which runs the other way.
/// </para>
/// </remarks>
public enum OrganizationRole
{
    /// <summary>
    /// Not a member of the organization in scope, or no organization is in scope.
    /// </summary>
    None = 0,

    /// <summary>
    /// An ordinary member.
    /// </summary>
    User = 1,

    /// <summary>
    /// Holds a hand-picked set of permissions rather than one of the fixed roles.
    /// </summary>
    Custom = 2,

    /// <summary>
    /// May administer the organization, but not billing or ownership.
    /// </summary>
    Admin = 3,

    /// <summary>
    /// May do anything, including removing other owners.
    /// </summary>
    Owner = 4,
}
