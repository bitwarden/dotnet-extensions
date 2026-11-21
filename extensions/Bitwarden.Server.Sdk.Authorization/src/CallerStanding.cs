using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// What the person behind a product service's request holds now, where their session names it: resolved once, before
/// authorization, by <see cref="CurrentStandingCheck"/>, and read by <see cref="CallerRole"/>.
/// </summary>
/// <param name="Role">Their role in the organization the session names; <see cref="OrganizationRole.None"/> in a session for a provider or one of its clients.</param>
/// <param name="Permissions">A custom member's permissions there.</param>
/// <param name="ProviderRole">Their role on the staff of the provider the session names; <see cref="ProviderRole.None"/> in a member's session.</param>
internal sealed record CallerStanding(
    OrganizationRole Role,
    IReadOnlySet<OrganizationPermission> Permissions,
    ProviderRole ProviderRole)
{
    /// <summary>
    /// A session naming no organization and no provider: nothing to hold.
    /// </summary>
    public static readonly CallerStanding Nothing = new(OrganizationRole.None, new HashSet<OrganizationPermission>(), ProviderRole.None);

    /// <summary>
    /// The standing resolved for the request, or null when none was.
    /// </summary>
    public static CallerStanding? Of(HttpContext http) => http.Features.Get<CallerStanding>();
}
