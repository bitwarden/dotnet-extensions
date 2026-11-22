using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// Notes the organization or provider a request's body names, for an operation declared to name one
/// (<see cref="NamesOrganizationAttribute"/>, <see cref="NamesProviderAttribute"/>).
/// </summary>
/// <remarks>
/// <para>
/// A product service authorizes such an operation against the owner it names when the session names none. An owner
/// named in the path is read from the route; one named in the body is known only once the body is bound, so whatever
/// binds it notes it here. The standard endpoints do this for every declared operation.
/// </para>
/// </remarks>
public static class RequestNamedOwners
{
    internal static readonly object OrganizationKey = new();

    internal static readonly object ProviderKey = new();

    /// <summary>
    /// Notes the organization the request's body names.
    /// </summary>
    /// <param name="http">The request being handled.</param>
    /// <param name="organizationId">The organization its body names.</param>
    public static void NoteOrganization(HttpContext http, Guid organizationId) =>
        http.Items[OrganizationKey] = organizationId;

    /// <summary>
    /// Notes the provider the request's body names.
    /// </summary>
    /// <param name="http">The request being handled.</param>
    /// <param name="providerId">The provider its body names.</param>
    public static void NoteProvider(HttpContext http, Guid providerId) =>
        http.Items[ProviderKey] = providerId;
}
