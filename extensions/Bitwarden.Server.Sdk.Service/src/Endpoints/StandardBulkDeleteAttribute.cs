using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Removes every resource an expression matches: <c>POST</c> to
/// <c>{collection}:bulk-delete</c>, succeeding with <c>200 OK</c>.
/// </summary>
/// <remarks>
/// <c>POST</c> rather than <c>DELETE</c>, because the expression selecting what to remove
/// travels in the body and a <c>DELETE</c> with a body is not something every intermediary
/// will carry. The filter is required, and on this operation especially: without one it would
/// remove everything in scope.
/// </remarks>
/// <param name="collection">
/// The collection's path, without a trailing slash, such as <c>api/v1/members</c>. See
/// <see cref="StandardBulkUpdateAttribute"/> for why it is given rather than derived.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardBulkDeleteAttribute(string collection)
    : HttpPostAttribute(BulkRoutes.For(collection, "bulk-delete")), IStandardOperation
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
