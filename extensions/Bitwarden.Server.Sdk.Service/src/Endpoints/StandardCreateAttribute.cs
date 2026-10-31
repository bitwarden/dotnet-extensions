using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Creates a resource: <c>POST</c> to the collection, succeeding with <c>201 Created</c>.
/// </summary>
/// <param name="template">
/// The route relative to the endpoint's group. Defaults to the collection itself, which is where a
/// create is posted.
/// </param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardCreateAttribute(string template = "") : HttpPostAttribute(template), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status201Created;
}
