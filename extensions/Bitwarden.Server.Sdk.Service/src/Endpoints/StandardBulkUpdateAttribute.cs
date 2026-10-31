using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Applies one change to every resource an expression matches:
/// <c>POST</c> to <c>{collection}:bulk-update</c>, succeeding with <c>200 OK</c>.
/// </summary>
/// <remarks>
/// <para>
/// The alternative to a caller making one request per resource. Confirming four hundred
/// members one at a time is four hundred round trips, four hundred transactions and four
/// hundred chances to stop halfway; this is one of each.
/// </para>
/// <para>
/// A <see cref="BulkUpdate{T}"/> carries the expression selecting what to change and the fields
/// to change, and the response is a count rather than the resources: returning them would make
/// it unbounded by anything the caller chose.
/// </para>
/// <para>
/// The filter is not optional and has no default, here or in the standards. An absent filter
/// would mean every resource in scope, which nobody meant to leave out.
/// </para>
/// </remarks>
/// <param name="collection">
/// The collection's path, without a trailing slash, such as <c>api/v1/members</c>. Given rather than
/// derived because the route is a suffix on the collection rather than a segment beneath it,
/// and a route group can only join with a slash. Declare it as a constant and use it for the
/// collection's <c>MapGroup</c> and for this endpoint's <c>Map</c> line
/// (<c>$"{Collection}:bulk-update"</c>), so they cannot drift; the service refuses to start
/// when the mapped route and this one disagree.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardBulkUpdateAttribute(string collection)
    : HttpPostAttribute(BulkRoutes.For(collection, "bulk-update")), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status200OK;

    /// <summary>
    /// The atomicity modes the operation supports. All-or-nothing unless it says otherwise.
    /// </summary>
    /// <remarks>
    /// The platform reads the caller's <c>atomic</c> parameter, checks it against this, and
    /// refuses a mode the operation does not offer with a <c>422</c>. Declared here so it
    /// cannot be forgotten: a bulk API that quietly ignored <c>atomic</c> would give a caller
    /// who asked for best-effort an all-or-nothing outcome, which is a different answer rather
    /// than a lesser one.
    /// </remarks>
    public Atomicity Supports { get; set; } = Atomicity.AllOrNothing;

    /// <inheritdoc cref="StandardBulkActionAttribute.Targeting"/>
    public Targeting Targeting { get; set; } = Targeting.Either;
}
