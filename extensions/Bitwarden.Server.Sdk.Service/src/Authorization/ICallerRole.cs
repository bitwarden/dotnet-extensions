namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// What the caller may do in the organization their session is scoped to.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="Bitwarden.Server.Sdk.Common.IContext"/>, and deliberately not
/// part of it. Context is <em>asserted</em> by the caller and says what a call is about; this is
/// <em>proven</em> by the session's signature and says what the caller may do. The two travel
/// differently, and the existing interface says so at length.
/// </para>
/// <para>
/// <b>For product services only.</b> A domain service never sees a user session (it terminates
/// at the product service, which presents its own service token downstream), so a domain service
/// reading this would find nothing and must not try. It enforces domain rules; deciding whether
/// the human behind the request was entitled to ask is the product layer's job.
/// </para>
/// <para>
/// The role and permissions are read from claims rather than fetched. That is not a shortcut
/// around asking the service that owns them: a signed, lifetime-bounded assertion by an
/// authority is a different thing from a copy of another service's row. The cost is that a
/// demoted administrator keeps their role until the session expires.
/// </para>
/// <para>
/// A role is about one organization, so it counts only for that one. The session names the
/// organization it was issued for in its <c>organization</c> claim, and the role and
/// permissions answer only when that is the organization the request is about. With no
/// organization in context, or another one, the caller is <see cref="OrganizationRole.None"/> and
/// holds nothing; otherwise an owner of one organization could act as an owner of another by
/// naming it in <c>Bitwarden-Organization</c>.
/// </para>
/// </remarks>
public interface ICallerRole
{
    /// <summary>
    /// The caller's role in the organization in scope.
    /// </summary>
    OrganizationRole Role { get; }

    /// <summary>
    /// The caller's role in the provider in scope.
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="Role"/> rather than an alternative to it. A provider staff
    /// member administering a client organization holds a provider role and, by design, no
    /// membership of the organization they are administering, which is exactly why the
    /// organization role cannot answer for them.
    /// </remarks>
    ProviderRole ProviderRole { get; }

    /// <summary>
    /// Whether the caller holds a permission.
    /// </summary>
    /// <param name="permission">The permission to test.</param>
    /// <returns>
    /// True when the caller's role reaches it. An owner or an administrator holds every
    /// permission; a custom member holds those granted to them; anybody else holds none.
    /// </returns>
    bool Holds(OrganizationPermission permission);
}
