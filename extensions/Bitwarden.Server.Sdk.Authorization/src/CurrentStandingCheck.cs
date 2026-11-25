using Bitwarden.Server.Sdk.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// At a product service, reads the person's current standing in what their session names, once per request and before
/// authorization, and refuses the request when they no longer hold it.
/// </summary>
/// <remarks>
/// <para>
/// Identity issues an organization's session only to a confirmed member, a client organization's only to confirmed
/// staff of the provider administering it, and a provider's only to its confirmed staff. That holds when the session is
/// exchanged and may stop holding within its lifetime. This asks again on every request (<see cref="ICurrentStanding"/>):
/// </para>
/// <list type="bullet">
///   <item><description>a session for an organization: the person's confirmed membership, whose role and permissions
///   <see cref="ICallerRole"/> then answers with;</description></item>
///   <item><description>a session for a provider's client organization (it names the organization and the provider):
///   that the provider still administers the organization, and the person's role on its confirmed staff;</description></item>
///   <item><description>a session for a provider: the person's role on its confirmed staff.</description></item>
/// </list>
/// <para>
/// Where the person no longer holds it, the request is refused before any handler, as the platform answers the same
/// person elsewhere:
/// </para>
/// <list type="bullet">
///   <item><description>no longer a confirmed member of the session's organization, or no longer confirmed staff of the
///   session's provider: the platform's <c>404</c> (<see cref="NotFoundException"/>, "Resource not found."), its answer
///   for an organization the person isn't a member of. It is also what a person refused a new session for the
///   organization sees, so a person removed sees the same answer within their old session's lifetime as after it;</description></item>
///   <item><description>a client organization's session whose person is no longer confirmed staff of the provider
///   administering it (or whose provider no longer does): <c>403</c>, <see cref="NotProviderStaff"/>. The platform checks
///   that standing on each request and forbids the request when it fails, whatever the session's claims say.</description></item>
/// </list>
/// <para>
/// An answer whose role is <c>None</c>, or a value the platform doesn't define, is no standing and refused the same way:
/// an undefined role would otherwise outrank every defined one where roles are compared.
/// </para>
/// <para>
/// Nothing is asked for a request with no person behind it (an organization credential, a machine account, a Send's
/// token), a session naming neither an organization nor a provider, or an operation open to anybody. Constructed when
/// the pipeline is built, so a product service with no <see cref="ICurrentStanding"/> registered fails at startup rather
/// than authorizing from what its sessions were issued with.
/// </para>
/// </remarks>
internal sealed class CurrentStandingCheck
{
    /// <summary>
    /// What a client organization's session is refused with when its person is no longer the provider's staff.
    /// </summary>
    internal const string NotProviderStaff = "The caller must be staff of the provider administering the organization.";

    private readonly RequestDelegate _next;

    /// <summary>
    /// Checks that the service can ask, before it serves anything.
    /// </summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="services">Whether <see cref="ICurrentStanding"/> is registered.</param>
    /// <exception cref="InvalidOperationException">No <see cref="ICurrentStanding"/> is registered.</exception>
    public CurrentStandingCheck(RequestDelegate next, IServiceProviderIsService services)
    {
        if (!services.IsService(typeof(ICurrentStanding)))
        {
            throw new InvalidOperationException(
                $"A product service authorizes a person from their current standing, and no {nameof(ICurrentStanding)} is "
                + "registered. Register the implementation the standing's owner provides.");
        }

        _next = next;
    }

    /// <summary>
    /// Resolves the standing, or refuses the request.
    /// </summary>
    /// <param name="http">The request.</param>
    public async Task InvokeAsync(HttpContext http)
    {
        if (http.GetEndpoint() is { } endpoint
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
            && CurrentContext.Subject(http) is { } person)
        {
            http.Features.Set(await ResolveAsync(http, person, http.RequestAborted));
        }

        await _next(http);
    }

    /// <summary>
    /// What the person holds now in what their session names.
    /// </summary>
    /// <exception cref="NotFoundException">No longer a member of its organization, or staff of its provider.</exception>
    /// <exception cref="ForbiddenException">No longer staff of the provider administering its client organization.</exception>
    private static async Task<CallerStanding> ResolveAsync(HttpContext http, Guid person, CancellationToken cancellationToken)
    {
        var organization = CallerRole.IssuedFor(http);
        var provider = CallerRole.ProviderIssuedFor(http);

        if (organization is null && provider is null)
        {
            return CallerStanding.Nothing;
        }

        var current = http.RequestServices.GetRequiredService<ICurrentStanding>();

        if (organization is { } named)
        {
            if (provider is { } administering)
            {
                // A provider's staff member's session for a client: the provider must still administer it, and the
                // person still be its confirmed staff. They hold no organization role there, as the session was issued.
                return await current.ProviderOfAsync(named, cancellationToken) == administering
                    && await current.ProviderRoleAsync(person, administering, cancellationToken) is { } staff
                    && Holds(staff)
                        ? CallerStanding.Nothing with { ProviderRole = staff }
                        : throw new ForbiddenException(NotProviderStaff);
            }

            return await current.MembershipAsync(person, named, cancellationToken) is { } member
                && Holds(member.Role)
                ? new CallerStanding(
                    member.Role,
                    member.Role == OrganizationRole.Custom ? member.Permissions : CallerStanding.Nothing.Permissions,
                    ProviderRole.None)
                : throw new NotFoundException();
        }

        return await current.ProviderRoleAsync(person, provider!.Value, cancellationToken) is { } role
            && Holds(role)
            ? CallerStanding.Nothing with { ProviderRole = role }
            : throw new NotFoundException();
    }

    /// <summary>
    /// Whether an answer's organization role is one a member can hold: neither <see cref="OrganizationRole.None"/> nor
    /// a value the platform doesn't define. Anything else is no membership, as <see cref="ICurrentStanding"/> promises.
    /// </summary>
    /// <param name="role">The role the answer gave.</param>
    private static bool Holds(OrganizationRole role) => role != OrganizationRole.None && Enum.IsDefined(role);

    /// <summary>
    /// Whether an answer's provider role is one a staff member can hold: neither <see cref="ProviderRole.None"/> nor a
    /// value the platform doesn't define. Anything else is no place on the staff.
    /// </summary>
    /// <param name="role">The role the answer gave.</param>
    private static bool Holds(ProviderRole role) => role != ProviderRole.None && Enum.IsDefined(role);
}
