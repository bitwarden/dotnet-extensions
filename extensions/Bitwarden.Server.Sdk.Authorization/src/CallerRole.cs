using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// Answers <see cref="ICallerRole"/> from the person's current standing in what their session names
/// (<see cref="CallerStanding"/>, resolved once per request by <see cref="CurrentStandingCheck"/>).
/// </summary>
/// <remarks>
/// The session still says where a role counts: the organization or provider it was issued for, and whether it is a
/// member's or a provider's staff member's. What the person holds there is read now, never from the role and
/// permission claims the session was issued with.
/// </remarks>
/// <param name="accessor">Supplies the request being handled.</param>
/// <param name="context">Says which organization the request is about.</param>
internal sealed class CallerRole(IHttpContextAccessor accessor, IContext context) : ICallerRole
{
    /// <summary>
    /// The claim naming the organization the session was issued for. The role and
    /// permission claims describe the caller in that organization and in no other.
    /// </summary>
    internal const string OrganizationClaim = "organization";

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="OrganizationRole.None"/> unless the session was issued for the organization in
    /// context, and then the person's role there now: a session naming no organization, or another one, holds none.
    /// <para>
    /// An organization credential (<see cref="AcceptsOrganizationCredentialAttribute"/>) is an
    /// <see cref="OrganizationRole.Owner"/> of its organization, as an organization's API key acts
    /// as its owner, and of no other.
    /// </para>
    /// </remarks>
    public OrganizationRole Role =>
        !IssuedForOrganizationInContext()
            ? OrganizationRole.None
            : accessor.HttpContext is { } http && OrganizationCredential.Accepted(http) is not null
                ? OrganizationRole.Owner
                : Standing()?.Role ?? OrganizationRole.None;

    /// <summary>
    /// The claim naming the provider a provider session was issued for.
    /// </summary>
    internal const string ProviderClaim = "provider";

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ProviderRole.None"/> unless the session is about where the request is: issued for
    /// the provider in context (its <c>provider</c> claim names it), or for the organization in
    /// context (a provider's staff member's session for a client organization), and then the
    /// person's role on that provider's confirmed staff now. A session naming neither, or another
    /// provider, holds none, as an organization role is; so does a member's session.
    /// </remarks>
    public ProviderRole ProviderRole =>
        IssuedForProviderInContext() || IssuedForOrganizationInContext()
            ? Standing()?.ProviderRole ?? ProviderRole.None
            : ProviderRole.None;

    /// <inheritdoc />
    public bool Holds(OrganizationPermission permission)
    {
        if (permission == OrganizationPermission.None)
        {
            return true;
        }

        return Role switch
        {
            OrganizationRole.Owner or OrganizationRole.Admin => true,
            OrganizationRole.Custom => Standing()?.Permissions.Contains(permission) == true,
            _ => false,
        };
    }

    /// <summary>
    /// The organization the current session was issued for, or null when it names none.
    /// </summary>
    internal static Guid? IssuedFor(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(OrganizationClaim)?.Value, out var organization)
            ? organization
            : null;

    private bool IssuedForOrganizationInContext() =>
        context.OrganizationId is { } organization
        && accessor.HttpContext is { } http
        && IssuedFor(http) == organization;

    /// <summary>
    /// The provider the current session was issued for, or for a client organization of: its <c>provider</c> claim.
    /// </summary>
    internal static Guid? ProviderIssuedFor(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(ProviderClaim)?.Value, out var provider)
            ? provider
            : null;

    private bool IssuedForProviderInContext() =>
        context.ProviderId is { } provider
        && accessor.HttpContext is { } http
        && ProviderIssuedFor(http) == provider;

    /// <summary>
    /// What the person holds now, or null where the request has no person behind it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A person's session naming an organization or provider whose standing wasn't resolved: a role read outside the
    /// pipeline <see cref="CurrentStandingCheck"/> runs in, or on an operation open to anybody. Never answered from the
    /// session's claims instead.
    /// </exception>
    private CallerStanding? Standing()
    {
        if (accessor.HttpContext is not { } http || CurrentContext.Subject(http) is null)
        {
            return null;
        }

        return CallerStanding.Of(http)
            ?? throw new InvalidOperationException(
                "The caller's current standing wasn't resolved for this request. A product service resolves it before "
                + "authorization (UseCurrentStanding), on every operation that isn't open to anybody.");
    }
}
