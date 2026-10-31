using System.Runtime.CompilerServices;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Invokes an action upon every targeted resource: <c>POST</c> to
/// <c>{collection}:bulk-{action}</c>, succeeding with <c>200 OK</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bulk form of <c>{collection}/{id}/actions/{action}</c>, and shorthand for calling it
/// once per resource. It is never a way around that action's rules: each resource is held to
/// the same authorization, validation and transition rules, and produces the same side
/// effects, as it would one at a time.
/// </para>
/// <para>
/// This is what a status change in bulk looks like. A field governed by an action is read-only,
/// so a bulk update cannot set it: confirming four hundred members is
/// <c>members:bulk-confirm</c>, not a bulk update writing <c>status</c>.
/// </para>
/// </remarks>
/// <param name="collection">
/// The collection's path, without a trailing slash, such as <c>api/v1/members</c>. See
/// <see cref="StandardBulkUpdateAttribute"/> for why it is given rather than derived.
/// </param>
/// <param name="action">
/// The action's name, matching the single-resource action it is the bulk form of. Defaults to
/// the method's own name, kebab-cased.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardBulkActionAttribute(
    string collection,
    [CallerMemberName] string action = "")
    : HttpPostAttribute(BulkRoutes.ForAction(collection, action)), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status200OK;

    /// <summary>
    /// The action's name, as it appears in the route after <c>bulk-</c>.
    /// </summary>
    public string Action { get; } = ResourceTypes.KebabCase(action);

    /// <inheritdoc cref="StandardBulkUpdateAttribute.Supports"/>
    public Atomicity Supports { get; set; } = Atomicity.AllOrNothing;

    /// <summary>
    /// How the caller may name the resources. Either way unless it says otherwise.
    /// </summary>
    public Targeting Targeting { get; set; } = Targeting.Either;
}
