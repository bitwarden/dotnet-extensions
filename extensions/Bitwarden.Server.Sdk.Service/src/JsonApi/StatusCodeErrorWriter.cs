using System.Net.Mime;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Writes the standard error document for a failure that produced a status code and no body.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ErrorResponseExceptionHandler"/> cannot cover these. A request for an address that
/// matches no route, or for a method a route does not serve, is refused by routing: nothing
/// throws, so no exception handler runs, and the response falls through to whatever
/// <c>UseStatusCodePages</c> is configured with. Left at its default that is an RFC 9457 problem
/// document, which contradicts the single shape <see cref="ErrorResponse"/> promises and that
/// every operation advertises as its <c>default</c> response.
/// </para>
/// <para>
/// It is worth getting right rather than dismissing as an edge case: a typo in a URL is among the
/// first failures any caller meets, and a generated client that knows only
/// <see cref="ErrorResponse"/> cannot read a problem document.
/// </para>
/// </remarks>
internal static class StatusCodeErrorWriter
{
    /// <summary>
    /// Writes the error document for the status already set on the response.
    /// </summary>
    /// <param name="context">The failing request.</param>
    public static Task Write(StatusCodeContext context)
    {
        var http = context.HttpContext;
        var status = http.Response.StatusCode;
        var (code, title, detail) = Describe(status, http.Request.Method);

        http.Response.ContentType = MediaTypeNames.Application.Json;

        return http.Response.WriteAsJsonAsync(
            new ErrorResponse
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
            },
            http.RequestAborted);
    }

    /// <remarks>
    /// A 404 here is a route that matches nothing, which is not quite the same as a resource that
    /// does not exist, but the two are deliberately indistinguishable to a caller, and reporting
    /// them with one code keeps it that way.
    /// </remarks>
    private static (string Code, string Title, string Detail) Describe(int status, string method) => status switch
    {
        StatusCodes.Status404NotFound => (ErrorCodes.NotFound, ErrorTitles.NotFound, ErrorDetails.NoSuchRoute),

        StatusCodes.Status405MethodNotAllowed => (
            ErrorCodes.MethodNotAllowed, ErrorTitles.MethodNotAllowed, ErrorDetails.MethodNotAllowed(method)),

        StatusCodes.Status406NotAcceptable => (
            ErrorCodes.NotAcceptable, ErrorTitles.NotAcceptable, ErrorDetails.NotAcceptable),

        StatusCodes.Status415UnsupportedMediaType => (
            ErrorCodes.UnsupportedMediaType, ErrorTitles.UnsupportedMediaType, ErrorDetails.UnsupportedMediaType),

        StatusCodes.Status401Unauthorized => (
            ErrorCodes.Unauthenticated, ErrorTitles.Unauthenticated, ErrorDetails.Unauthenticated),

        StatusCodes.Status403Forbidden => (ErrorCodes.Forbidden, ErrorTitles.Forbidden, ErrorDetails.Forbidden),

        >= StatusCodes.Status500InternalServerError => (
            ErrorCodes.InternalError, ErrorTitles.InternalError, ErrorDetails.InternalError),

        _ => (ErrorCodes.InvalidRequest, ErrorTitles.InvalidRequest, ErrorDetails.InvalidRequest),
    };
}
