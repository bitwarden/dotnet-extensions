namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// Who and what the current operation concerns.
/// </summary>
/// <remarks>
/// <para>
/// Two questions, and both may legitimately answer no: is there a current organization, and is
/// there a current user. A request for data belonging to no organization has neither; a
/// background job or an event consumer acts for no user.
/// </para>
/// <para>
/// Deliberately not about authentication. A token says which service is calling and what it is
/// permitted to do, and that is the authorization layer's business; this says what the call is
/// <em>about</em>. Keeping them apart is what lets one interface serve both sides — a service
/// reads its caller's context, a client writes its own into the calls it makes, and neither needs
/// members that mean nothing on its side.
/// </para>
/// <para>
/// Both values are <em>asserted</em> by the caller rather than proven. A service holding a valid
/// token is trusted to say what it is acting for; binding those assertions cryptographically is
/// an open platform decision.
/// </para>
/// </remarks>
public interface IContext
{
    /// <summary>The organization this operation concerns, if any.</summary>
    Guid? OrganizationId { get; }

    /// <summary>The user this operation is made on behalf of, if any.</summary>
    Guid? UserId { get; }

    /// <summary>
    /// The provider on whose authority this operation is made, if any.
    /// </summary>
    /// <remarks>
    /// A provider — a managed service provider, a reseller, a business unit — administers
    /// organizations it is not a member of. Which provider a request is made on behalf of is
    /// underivable in the same way the organization is: no query answers it, so it must be
    /// told or the request cannot be scoped. Present alongside the other two rather than
    /// instead of either, because a provider administering a client organization is all three
    /// facts at once.
    /// </remarks>
    Guid? ProviderId { get; }

    /// <summary>
    /// The address the originating request came from, if known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the product service that received the user's request can see it, and a domain
    /// service cannot find it out — so it travels, as the organization does, or an audit record
    /// loses where an action came from. Asserted by the caller like every other member here, so
    /// a domain service trusts it exactly as far as it trusts the user and organization the same
    /// caller names: an IP allowlist may gate on it, as it would on the address its own edge
    /// resolved.
    /// </para>
    /// <para>
    /// Defaulted, so an implementation predating it keeps compiling and answers "unknown".
    /// </para>
    /// </remarks>
    string? ClientIpAddress => null;

    /// <summary>
    /// The kind of client the originating request was made from, if known.
    /// </summary>
    /// <remarks>
    /// Underivable for the same reason as the address, and carried for the same purpose. Opaque
    /// to the platform — a short name the originating product service chose — because the
    /// vocabulary of client kinds belongs to the products, not to the plumbing that carries it.
    /// </remarks>
    string? DeviceType => null;

    /// <summary>
    /// The device the originating request was made from, by the identifier its client generated
    /// for itself, if known.
    /// </summary>
    /// <remarks>
    /// What Bitwarden's <c>ExcludeCurrentContext</c> push option reads: a change is pushed to every device
    /// but the one it came from, which already has it. Underivable past the edge, like the device
    /// type, so it travels the same way. Never proof of anything — a caller can name any device —
    /// and nothing may be gated on it; the worst a false one does is spare a device a push.
    /// </remarks>
    string? DeviceIdentifier => null;

    /// <summary>
    /// The kind of credential the request that started this work presented, if known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set by the product service from the token it validated (a person's session, an organization's
    /// API key, a machine account's token), and <see cref="CallerCredential.System"/> on a call a
    /// service makes as itself (<c>OutgoingCall.AsSystem</c>); carried on from there in
    /// <see cref="BitwardenHeaders.Context"/>.
    /// </para>
    /// <para>
    /// <b>Audit-only.</b> No authorization, row filter or cache may read it: it is asserted like the
    /// rest of this interface, and says how an action was taken, never whether it may be.
    /// </para>
    /// </remarks>
    CallerCredential? Credential => null;

    /// <summary>
    /// On a <see cref="CallerCredential.System"/> call, the person the audit record names as having acted, if any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some of what Bitwarden does as the system, in the middle of somebody's request, it still
    /// records against that somebody: the policies trusted device encryption turns on, the
    /// memberships an emergency-access takeover removes. The call itself names nobody acting
    /// (<see cref="UserId"/> is null, so no actor rule is asked of it), and this says whom the record
    /// names (<c>OutgoingCall.AsSystem(organization, recordedAs:)</c>).
    /// </para>
    /// <para>
    /// <b>Audit-only</b>, as <see cref="Credential"/> is.
    /// </para>
    /// </remarks>
    Guid? RecordedUserId => null;

    /// <summary>
    /// The provider whose staff member is acting in one of its client organizations, if that is who is acting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The organization's events record it beside the staff member (their <c>ProviderId</c>,
    /// <c>ProviderIdForOrg</c>). A staff member's session for a client is about the organization, so
    /// <see cref="ProviderId"/> is null and their calls name no provider; this carries it for the record alone.
    /// </para>
    /// <para>
    /// <b>Audit-only</b>, as <see cref="Credential"/> is.
    /// </para>
    /// </remarks>
    Guid? RecordedProviderId => null;

    /// <summary>
    /// Every organization the acting person is a confirmed member of, if known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>organizations</c> list Identity puts on every access token, beside its organization
    /// claims. A product service reads it from the
    /// person's session; below the product layer the session does not travel, so the product service
    /// carries it in <see cref="BitwardenHeaders.Context"/> on the calls it makes as that person, and a
    /// domain service reads it from there.
    /// </para>
    /// <para>
    /// <b>Targeting-only.</b> Read to evaluate feature flags, where it is set as <c>organizations</c>
    /// on the LaunchDarkly user context. It grants nothing: no authorization,
    /// row filter, cache or audit record may read it. Membership and role come from the session's
    /// <c>organization</c> claim and from the Organization Service.
    /// </para>
    /// </remarks>
    IReadOnlyList<Guid>? UserOrganizationIds => null;
}
