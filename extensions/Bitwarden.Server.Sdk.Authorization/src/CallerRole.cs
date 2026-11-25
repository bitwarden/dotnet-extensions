using Bitwarden.Server.Sdk.Context;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// Reads <see cref="ICallerRole"/> from the claims of the current session.
/// </summary>
/// <param name="accessor">Supplies the request being handled.</param>
/// <param name="context">Says which organization the request is about.</param>
internal sealed class CallerRole(IHttpContextAccessor accessor, IContext context) : ICallerRole
{
    /// <summary>
    /// The claim naming the organization the session was issued for. The role and
    /// permission claims describe the caller in that organization and in no other.
    /// </summary>
    internal const string OrganizationClaim = "organization";

    /// <summary>
    /// The claim naming the caller's role.
    /// </summary>
    internal const string RoleClaim = "organization_role";

    /// <summary>
    /// The claim naming one permission the caller holds. Repeated, one per permission.
    /// </summary>
    internal const string PermissionClaim = "organization_permission";

    /// <summary>
    /// The claim naming the caller's role in the provider.
    /// </summary>
    internal const string ProviderRoleClaim = "provider_role";

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="OrganizationRole.None"/> unless the session was issued for the organization in
    /// context. A role claim on a session naming no organization, or another one, is ignored.
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
                : Named<OrganizationRole>(Claim(RoleClaim), out var role)
                    ? role
                    : OrganizationRole.None;

    /// <summary>
    /// The claim naming the provider a provider session was issued for.
    /// </summary>
    internal const string ProviderClaim = "provider";

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="ProviderRole.None"/> unless the session is about where the request is: issued for
    /// the provider in context (its <c>provider</c> claim names it), or for the organization in
    /// context (a provider's staff member's session for a client organization). A
    /// provider role on a session naming neither, or another provider, is ignored, as an
    /// organization role is.
    /// </remarks>
    public ProviderRole ProviderRole =>
        (IssuedForProviderInContext() || IssuedForOrganizationInContext())
        && Named<ProviderRole>(Claim(ProviderRoleClaim), out var role)
            ? role
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
            OrganizationRole.Custom => Granted().Contains(permission.ToString(), StringComparer.OrdinalIgnoreCase),
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

    private bool IssuedForProviderInContext() =>
        context.ProviderId is { } provider
        && Guid.TryParse(Claim(ProviderClaim), out var issued)
        && issued == provider;

    /// <summary>
    /// A role claim naming one role, by name and in any case. A number or a comma-separated list, which
    /// <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/> would also accept, names none.
    /// </summary>
    private static bool Named<TRole>(string? value, out TRole role)
        where TRole : struct, Enum
    {
        role = default;
        return value is { Length: > 0 }
            && value.All(char.IsAsciiLetter)
            && Enum.TryParse(value, ignoreCase: true, out role)
            && Enum.IsDefined(role);
    }

    private string? Claim(string type) =>
        accessor.HttpContext?.User.FindFirst(type)?.Value;

    private IEnumerable<string> Granted() =>
        accessor.HttpContext?.User.FindAll(PermissionClaim).Select(claim => claim.Value)
        ?? [];
}
