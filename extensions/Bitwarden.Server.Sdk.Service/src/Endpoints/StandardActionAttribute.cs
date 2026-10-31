using System.Runtime.CompilerServices;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Performs a named action on a resource: <c>POST</c> to
/// <c>{id}/actions/{action}</c>, succeeding with <c>200 OK</c>, or <c>204 No Content</c> when
/// there is nothing to return.
/// </summary>
/// <remarks>
/// <para>
/// For the things a resource does that are not a change to its fields. Confirming a member,
/// revoking them, sending somebody an email: each has its own rules, its own permission, and
/// often its own payload, and none of them is expressible as "replace this resource with that
/// one". Modelling them as writes to a status field makes a caller responsible for knowing
/// which values are reachable from where, and makes one permission cover four operations that
/// should not share one.
/// </para>
/// <para>
/// The action's name comes from the method's, in kebab case (<c>SendEmail</c> is
/// <c>/actions/send-email</c>), so the route cannot drift from the code. Pass a name to
/// override that where the method cannot be called what the action is.
/// </para>
/// <para>
/// The payload, if there is one, is a plain JSON object rather than a resource document. It is
/// not a resource: nothing is being created or replaced, and wrapping arguments in
/// <c>data</c>/<c>attributes</c> would claim otherwise. What comes back <em>is</em> a resource
/// document when the action returns the resource it acted on.
/// </para>
/// <example>
/// <code>
/// [StandardAction]
/// public async Task&lt;ResourceDocument&lt;Member&gt;&gt; Revoke(
///     string id,
///     RevokeMember request,
///     CancellationToken cancellationToken)
/// </code>
/// </example>
/// </remarks>
/// <param name="action">
/// The action's name in the route, kebab-cased. Left alone it is the method's own name, which
/// the compiler supplies, so the route cannot drift from the code: <c>SendEmail</c> is
/// <c>send-email</c>, and an acronym stays one word (<c>VerifySsoToken</c> and
/// <c>VerifySSOToken</c> are both <c>verify-sso-token</c>). Pass a name where the method cannot be
/// called what the action is. A name passed is kebab-cased too, so <c>sendEmail</c> and
/// <c>send-email</c> publish the same route and neither can produce a non-conforming one.
/// </param>
/// <param name="template">
/// The resource the action is on, relative to the endpoint's group. Defaults to <c>{id}</c>, a
/// resource in the collection. A singleton the caller's scope names gives its literal instead:
/// <c>[StandardAction("change-kdf", template: "current")]</c> publishes
/// <c>current/actions/change-kdf</c>, as the standards' <c>current</c> resource requires. Only
/// the resource is given: the <c>actions/{action}</c> that follows it is the platform's, so no
/// action can be published anywhere else.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardActionAttribute([CallerMemberName] string action = "", string template = "{id}")
    : HttpPostAttribute(Route(template, action)), IStandardOperation
{
    /// <summary>
    /// The action's name, as it appears in the route.
    /// </summary>
    /// <remarks>
    /// Not <c>Name</c>: <see cref="Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute"/>
    /// already has one, and it means the route's name rather than the action's.
    /// </remarks>
    public string Action { get; } = ResourceTypes.KebabCase(action);

    /// <inheritdoc />
    /// <remarks>
    /// The floor rather than the answer. An action that returns nothing is a <c>204</c>, which
    /// the convention settles once it can see the method's return type.
    /// </remarks>
    public int SuccessStatusCode => StatusCodes.Status200OK;

    /// <remarks>
    /// Built in the attribute rather than in a convention, and that is not a preference. The
    /// XML comment support .NET generates keys an operation by its route at compile time, so
    /// two actions that both start life at the group's own route collide there, long
    /// before a convention could tell them apart.
    /// </remarks>
    private static string Route(string template, string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        return $"{template.Trim('/')}/actions/{ResourceTypes.KebabCase(action)}";
    }
}
