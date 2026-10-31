using System.Net.Mime;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Renders an authentication or authorization failure as the standard error document.
/// </summary>
/// <remarks>
/// Without this the framework writes a bare <c>401</c> or <c>403</c> with no body, which
/// contradicts the <c>default</c> response every operation advertises: a generated client would
/// have nothing to deserialise on the two failures it is most likely to meet.
/// </remarks>
internal sealed class AuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var challenged = authorizeResult.Challenged;

        var (status, code, title, detail) = challenged
            ? (StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated,
                ErrorTitles.Unauthenticated, ErrorDetails.Unauthenticated)
            : (StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
                ErrorTitles.Forbidden, Reason(authorizeResult) ?? ErrorDetails.Forbidden);

        context.Response.StatusCode = status;
        context.Response.ContentType = MediaTypeNames.Application.Json;

        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Errors =
            [
                new Error
                {
                    Status = status.ToString(),
                    Code = code,
                    Title = title,
                    Detail = detail,
                },
            ],
        });
    }

    /// <summary>
    /// The first reason a handler gave, when it gave one.
    /// </summary>
    /// <remarks>
    /// Safe to return: these say which scope or header the operation needs, which a caller must
    /// know to succeed, and none of them disclose anything about the resource.
    /// </remarks>
    private static string? Reason(PolicyAuthorizationResult result) =>
        result.AuthorizationFailure?.FailureReasons.FirstOrDefault()?.Message;
}
