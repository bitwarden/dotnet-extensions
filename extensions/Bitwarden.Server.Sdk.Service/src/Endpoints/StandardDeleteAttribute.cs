using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Removes a resource: <c>DELETE</c> by identifier, succeeding with <c>204 No Content</c>.
/// </summary>
/// <param name="template">The route relative to the endpoint's group. Defaults to the identifier.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardDeleteAttribute(string template = "{id}") : HttpDeleteAttribute(template), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status204NoContent;
}
