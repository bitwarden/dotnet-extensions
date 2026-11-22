using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// A check made before binding.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ParityGateAttribute : Attribute, IAuthorizesOperation
{
    /// <inheritdoc />
    public ValueTask AuthorizeAsync(HttpContext context) =>
        context.Request.Headers["X-Gate"] == "closed" ? throw new ForbiddenException("The gate is closed.") : ValueTask.CompletedTask;
}
