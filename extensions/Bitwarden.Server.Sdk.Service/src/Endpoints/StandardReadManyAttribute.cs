using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Reads a collection: <c>GET</c> on the collection, succeeding with <c>200 OK</c>.
/// </summary>
/// <remarks>
/// A collection read succeeds within the caller's scope even when nothing matches: an empty
/// <c>data</c> array, never a <c>404</c>.
/// </remarks>
/// <param name="template">The route relative to the endpoint's group. Defaults to the collection itself.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardReadManyAttribute(string template = "") : HttpGetAttribute(template), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status200OK;

    /// <inheritdoc />
    public bool IsRead => true;
}
