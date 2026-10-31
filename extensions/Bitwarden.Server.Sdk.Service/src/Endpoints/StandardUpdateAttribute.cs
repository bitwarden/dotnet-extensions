using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Replaces a resource: <c>PUT</c> by identifier, succeeding with <c>200 OK</c>.
/// </summary>
/// <param name="template">The route relative to the endpoint's group. Defaults to the identifier.</param>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardUpdateAttribute(string template = "{id}") : HttpPutAttribute(template), IStandardOperation
{
    /// <inheritdoc />
    public int SuccessStatusCode => StatusCodes.Status200OK;
}
