using Bitwarden.Server.Sdk.Service.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Reads the resources an expression matches: <c>POST</c> to <c>{collection}:search</c>,
/// succeeding with <c>200 OK</c>.
/// </summary>
/// <remarks>
/// <para>
/// For what <c>filter[…]</c> parameters cannot say. They combine with "and" and live in a URL
/// of limited length; a question such as "everything this machine account did or had done to
/// it" needs an "or", and a long one needs a body. The standards' answer is this operation,
/// carrying a JSON Logic expression in a <see cref="Search{T}"/>.
/// </para>
/// <para>
/// Otherwise a read-many in every respect: the same query parameters, the same paging and the
/// same document back, so a caller who outgrows the query string changes the verb and nothing
/// else. <c>POST</c> only because the question travels in the body; it changes nothing.
/// </para>
/// </remarks>
/// <param name="collection">
/// The collection's path, without a trailing slash, such as <c>api/v1/events</c>. See
/// <see cref="StandardBulkUpdateAttribute"/> for why it is given rather than derived.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardSearchAttribute(string collection)
    : HttpPostAttribute(BulkRoutes.For(collection, "search")), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status200OK;

    /// <inheritdoc />
    public bool IsRead => true;
}
