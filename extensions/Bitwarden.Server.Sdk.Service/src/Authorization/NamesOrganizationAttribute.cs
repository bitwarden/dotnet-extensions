using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Declares that a product service's operation names, in its path or its body, an organization the
/// person's session was not issued for, and carries it on its downstream calls.
/// </summary>
/// <remarks>
/// <para>
/// A product service takes the organization from the person's session, and a browser sends nothing
/// else. Some operations are about an organization the person can't hold a session
/// for: an invitee accepting, an accepted member not yet confirmed reading the policies they must
/// keep or enrolling in account recovery, anybody signed in reading an organization's public key.
/// Those name it in the request: in the path, as <c>{organizationId}</c>, when it names the resource
/// (<c>GET /api/v1/organizations/{organizationId}/public-key</c>), or in the body, as a request type
/// implementing <see cref="INamesOrganization"/>, when it is an action's input.
/// </para>
/// <para>
/// On such an operation:
/// <list type="bullet">
/// <item>The session must name no organization (<c>Organization = Forbidden</c>) and must name a
/// person (<c>User = Required</c>), and the operation demands no role or permission: the person
/// holds none in the organization named. The service refuses to start otherwise.</item>
/// <item>The organization never enters this service's own <see cref="IContext"/>: authorization never
/// sees it, and nothing a role would allow is allowed. It is carried only on the operation's
/// downstream calls (<see cref="IRequestNamedOwner"/>), so the domain service's row filter keeps the
/// request inside it and its own authority decides (an invitation's token, the membership).</item>
/// </list>
/// </para>
/// <para>
/// Recognised only in a product service; a domain service takes the organization from its caller.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NamesOrganizationAttribute : Attribute
{
    /// <summary>
    /// The route value an operation names the organization in.
    /// </summary>
    public const string RouteValue = "organizationId";
}
