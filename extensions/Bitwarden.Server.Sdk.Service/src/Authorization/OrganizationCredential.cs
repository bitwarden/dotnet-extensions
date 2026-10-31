using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Recognises an organization credential on a request whose operation accepts one.
/// </summary>
internal static class OrganizationCredential
{
    /// <summary>
    /// The organization a token is a credential for: an <c>organization</c> claim that is an
    /// identifier, and no <c>sub</c> at all.
    /// </summary>
    /// <remarks>
    /// No <c>sub</c> rather than a <c>sub</c> that isn't a person's: a person's organization session
    /// carries both, and is a person's.
    /// </remarks>
    internal static Guid? Of(ClaimsPrincipal principal) =>
        principal.FindFirst(Context.SubjectClaim) is null
        && Guid.TryParse(principal.FindFirst(CallerRole.OrganizationClaim)?.Value, out var organization)
            ? organization
            : null;

    /// <summary>
    /// What the operation being called declares, or null when it accepts no organization credential.
    /// </summary>
    internal static AcceptsOrganizationCredentialAttribute? DeclaredBy(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<AcceptsOrganizationCredentialAttribute>();

    /// <summary>
    /// The organization of an authenticated organization credential calling an operation that
    /// accepts one; null for anything else, a person's session included.
    /// </summary>
    internal static Guid? Accepted(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true && DeclaredBy(http) is not null
            ? Of(http.User)
            : null;
}
