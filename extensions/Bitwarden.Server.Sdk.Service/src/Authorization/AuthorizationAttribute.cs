using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// States what a caller must satisfy to invoke an operation.
/// </summary>
/// <remarks>
/// <para>
/// One attribute rather than several, so there is one thing to learn and one place to discover new
/// options as the model grows.
/// </para>
/// <para>
/// Every operation is authenticated whether or not it carries this attribute (the platform
/// applies a fallback policy), so the attribute is for saying something <em>more</em> than the
/// default, or for opting out of it.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Authorization(Scope = UserScopes.Write)]                              // authenticated, scoped
/// [Authorization(Scope = UserScopes.Read, Organization = ContextRequirement.Optional)]
/// [Authorization(Anonymous = true)]                                      // open, e.g. a probe
///
/// // A product service, which additionally knows who the human is:
/// [Authorization(Scope = MemberScopes.Write, Permission = OrganizationPermission.ManageUsers)]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AuthorizationAttribute : AuthorizeAttribute, IAuthorizationRequirementData
{
    /// <summary>
    /// The scope the caller's token must carry.
    /// </summary>
    /// <remarks>
    /// Authorization is on scope, never on the caller's name. Which service may hold a scope is
    /// decided once, where credentials are issued, so a service never learns who its callers are
    /// and the access matrix stays in one place.
    /// </remarks>
    public string? Scope { get; set; }

    /// <summary>
    /// Whether the request must name an organization.
    /// </summary>
    /// <remarks>
    /// Unset means "whatever the handler's class or the service says", which is what lets an
    /// operation state only where it differs. The platform's own default is
    /// <see cref="ContextRequirement.Required"/>.
    /// </remarks>
    public ContextRequirement Organization { get; set; }

    /// <summary>
    /// Whether the request must name the user it is made for. Unset inherits.
    /// </summary>
    public ContextRequirement User { get; set; }

    /// <summary>
    /// Whether the request must name a provider. Forbidden by default.
    /// </summary>
    /// <remarks>
    /// Forbidden unless declared. The provider travels only when the context has
    /// one (a provider's own session, and what is done on its behalf), so an operation a provider's
    /// session reaches says <c>Optional</c>, and one whose data belongs to a provider says
    /// <c>Required</c>, its rows saying <c>[OwnedBy(Owner.Provider)]</c>. Everything else refuses a
    /// provider rather than answering it as if it weren't there. A provider's staff member
    /// administering a client organization works in the organization's session, which carries the
    /// organization and no provider.
    /// </remarks>
    public ContextRequirement Provider { get; set; }

    /// <summary>
    /// The least organization role the caller must hold. <b>Product services only.</b>
    /// </summary>
    /// <remarks>
    /// Satisfied by that role or any above it, so <c>Admin</c> admits an owner. A domain service
    /// must leave this alone: it never sees a user session (the session terminates at the
    /// product service, which calls downstream with its own service token), so every caller
    /// would look like <see cref="OrganizationRole.None"/> and every operation would be refused.
    /// </remarks>
    public OrganizationRole OrganizationRole { get; set; } = OrganizationRole.None;

    /// <summary>
    /// The permission the caller must hold. <b>Product services only.</b>
    /// </summary>
    /// <remarks>
    /// An owner or an administrator satisfies any permission by their role; a custom member
    /// must have been granted it. The same warning as
    /// <see cref="OrganizationRole"/> applies: this is meaningless below the product layer.
    /// </remarks>
    public OrganizationPermission Permission { get; set; } = OrganizationPermission.None;

    /// <summary>
    /// The least provider role the caller must hold. <b>Product services only.</b>
    /// </summary>
    /// <remarks>
    /// Satisfied by that role or any above it, so <c>ServiceUser</c> admits a provider
    /// administrator. Independent of <see cref="OrganizationRole"/>: a provider staff member
    /// administering a client organization is not a member of it, so requiring both would
    /// refuse exactly the caller this exists for.
    /// </remarks>
    public ProviderRole ProviderRole { get; set; } = ProviderRole.None;

    /// <summary>
    /// Whether the operation may be called without a token at all.
    /// </summary>
    /// <remarks>
    /// For the operational surface (probes and documentation), which infrastructure reaches
    /// before any credential exists. Nothing that touches a caller's data should set this.
    /// </remarks>
    public bool Anonymous { get; set; }

    /// <summary>
    /// Whether this is the settled attribute the convention produced.
    /// </summary>
    /// <remarks>
    /// Only the settled one demands anything. A service, a handler's class and an operation may
    /// each declare part of the answer, and requirements compose as <em>and</em>, so if every one
    /// of them produced a requirement, an operation could tighten its class but never relax
    /// it. The convention reads all three, settles one, and that is the only one the pipeline
    /// ever sees.
    /// </remarks>
    internal bool Settled { get; init; }

    /// <inheritdoc />
    public IEnumerable<IAuthorizationRequirement> GetRequirements() =>
        Settled ? [new ContextRequirements(this)] : [];
}
