using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Replaces many resources wholesale: <c>POST</c> to <c>{collection}:bulk-replace</c>,
/// succeeding with <c>200 OK</c>.
/// </summary>
/// <remarks>
/// <c>PUT</c> once per resource, with the same replace-everything semantics: a field left out
/// is cleared rather than kept. That is what separates it from a bulk update, which names only
/// the fields it changes and leaves the rest alone.
/// </remarks>
/// <param name="collection">The collection's path, without a trailing slash.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardBulkReplaceAttribute(string collection)
    : HttpPostAttribute(BulkRoutes.For(collection, "bulk-replace")), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status200OK;

    /// <inheritdoc cref="StandardBulkUpdateAttribute.Supports"/>
    public Atomicity Supports { get; set; } = Atomicity.AllOrNothing;
}
