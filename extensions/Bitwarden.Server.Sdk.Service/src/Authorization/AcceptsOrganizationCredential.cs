namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Declares that a product service's operation also accepts an <b>organization credential</b>: a
/// token issued to an organization rather than to a person, holding <see cref="Scope"/>.
/// </summary>
/// <remarks>
/// <para>
/// A product service accepts only a person's session by default: its acting user is the token's
/// <c>sub</c>, and a token naming no person is refused <c>401</c> (<see cref="PersonSessionTokens"/>).
/// An organization's API key is the exception the Public API serves: a customer's script or
/// directory sync acting as the organization, with nobody behind it. Identity issues it a token
/// carrying <c>organization</c> and no <c>sub</c> (<c>client_credentials</c> for
/// <c>organization.{id}</c>). An operation carrying this attribute accepts that token too; every
/// other operation keeps refusing it.
/// </para>
/// <para>
/// What such a request is:
/// <list type="bullet">
/// <item>Its organization is the token's <c>organization</c>. A request carrying
/// <c>Bitwarden-Organization</c>, naming that organization or another, is refused <c>400</c>, as
/// any request to a product service carrying a caller-context header is
/// (<see cref="CallerContextRefusal"/>).</item>
/// <item>It has no user. The operation's <c>User</c> requirement does not apply: no person is acting,
/// and the declaration says so.</item>
/// <item>The caller is an <see cref="OrganizationRole.Owner"/> of that organization and nothing
/// else, as an organization's API key acts as its owner with no user, so every role and
/// permission requirement an owner meets, it meets.</item>
/// <item>The scope it must hold is <see cref="Scope"/>, in place of the operation's own: an
/// organization credential carries one scope of its own (<c>api.organization</c>) and none of a
/// person's.</item>
/// <item>Downstream calls carry <c>Bitwarden-Organization</c> and no <c>Bitwarden-User</c>, so the
/// domain services skip their actor rules, as they do for an API key.</item>
/// </list>
/// </para>
/// <para>
/// Recognised only in a product service. Below the product layer every caller is a service, its
/// tokens name nobody, and the header is the caller's assertion: there is nothing to accept.
/// </para>
/// </remarks>
/// <param name="scope">The scope an organization credential must hold to call the operation.</param>
/// <example>
/// <code>
/// [Authorization(Permission = OrganizationPermission.ManageUsers)]
/// [AcceptsOrganizationCredential("api.organization")]
/// [StandardUpdate]
/// public async Task&lt;ResourceDocument&lt;Member&gt;&gt; UpdateMember(...)
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AcceptsOrganizationCredentialAttribute(string scope) : Attribute
{
    /// <summary>
    /// The scope an organization credential must hold to call the operation.
    /// </summary>
    public string Scope { get; } = string.IsNullOrWhiteSpace(scope)
        ? throw new ArgumentException("An organization credential is accepted under a scope.", nameof(scope))
        : scope;
}
