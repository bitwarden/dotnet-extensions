using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Creates many resources: <c>POST</c> to <c>{collection}:bulk-create</c>, succeeding with
/// <c>200 OK</c>.
/// </summary>
/// <remarks>
/// Targeted by <c>data</c>, since nothing exists yet to name, and each item is shaped as the
/// single-resource create expects it. The response should carry the created resources, in
/// request order, because the caller needs the identifiers it did not choose.
/// </remarks>
/// <param name="collection">The collection's path, without a trailing slash.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardBulkCreateAttribute(string collection)
    : HttpPostAttribute(BulkRoutes.For(collection, "bulk-create")), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status200OK;

    /// <inheritdoc cref="StandardBulkUpdateAttribute.Supports"/>
    public Atomicity Supports { get; set; } = Atomicity.AllOrNothing;
}
