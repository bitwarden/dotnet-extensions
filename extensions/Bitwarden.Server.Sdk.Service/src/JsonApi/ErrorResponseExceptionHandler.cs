using System.Net.Mime;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service.JsonApi;

/// <summary>
/// Turns any exception that reaches the pipeline into the standard error document.
/// </summary>
/// <remarks>
/// <para>
/// The last thing between a fault and the caller. Without it an unhandled exception is rendered as
/// an RFC 9457 problem document, which contradicts the <c>default</c> response every operation
/// advertises, so a generated client would fail to deserialise precisely when something has
/// already gone wrong.
/// </para>
/// <para>
/// Unrecognized exceptions become a <c>500</c> that describes nothing. The cause is logged against
/// the same identifier the response carries, so an operator can find it without the caller being
/// told anything a caller cannot act on.
/// </para>
/// </remarks>
/// <param name="logger">Records each fault against the identifier its response carries.</param>
/// <param name="translators">Say which platform exception a library's exception means.</param>
internal sealed class ErrorResponseExceptionHandler(
    ILogger<ErrorResponseExceptionHandler> logger,
    IEnumerable<IExceptionTranslator> translators)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // A response already on its way cannot be replaced; let the host abort it rather than
        // write a second, malformed one on top.
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        // A library's failure that means one of the platform's is answered as that one. The log
        // keeps the original, so it still shows what happened.
        var answered = translators
            .Select(translator => translator.Translate(exception))
            .FirstOrDefault(translated => translated is not null) ?? exception;

        var errorId = httpContext.TraceIdentifier;

        // A service's own exception may already know what to say, and may have more than one
        // thing to say. Asked first, so the platform's single-error mapping is the fallback
        // rather than the ceiling.
        if (answered is IProvideErrors provided)
        {
            Log(provided.StatusCode, exception, errorId, httpContext);
            await Write(httpContext, provided.StatusCode, provided.Errors, cancellationToken);

            return true;
        }

        var (status, code, title, detail, pointer) = Describe(answered);

        Log(status, exception, errorId, httpContext);

        await Write(
            httpContext,
            status,
            [
                new Error
                {
                    // Only a fault of ours is worth quoting back to us. A 404 tells the caller
                    // everything they can act on already.
                    Id = status >= StatusCodes.Status500InternalServerError ? errorId : null,
                    Status = status.ToString(),
                    Code = code,
                    Title = title,
                    Detail = detail,
                    Source = pointer is null ? null : new ErrorSource { Pointer = pointer },
                },
            ],
            cancellationToken);

        return true;
    }

    private static async Task Write(
        HttpContext httpContext,
        int status,
        IReadOnlyList<Error> errors,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = MediaTypeNames.Application.Json;

        await httpContext.Response.WriteAsJsonAsync(new ErrorResponse { Errors = errors }, cancellationToken);
    }

    /// <summary>
    /// Maps an exception to the response it deserves.
    /// </summary>
    /// <remarks>
    /// An exception the platform recognises can say something useful, because a service raised it
    /// deliberately. Anything else is a bug, and a bug's detail is for the log rather than the
    /// caller: what would help them diagnose it would help an attacker just as much.
    /// </remarks>
    internal static (int Status, string Code, string Title, string Detail, string? Pointer) Describe(Exception exception) =>
        exception switch
        {
            NotFoundException => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                ErrorTitles.NotFound,
                exception.Message,
                null),

            // A rule that says who may act, rather than what a request may contain. Reached only
            // when a command raises it (the pipeline answers its own authorization long before
            // here), and without this it would be reported as a fault of ours.
            ForbiddenException => (
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                ErrorTitles.Forbidden,
                exception.Message,
                null),

            UnauthenticatedException => (
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthenticated,
                ErrorTitles.Unauthenticated,
                exception.Message,
                null),

            ConflictException => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                ErrorTitles.Conflict,
                exception.Message,
                null),

            InvalidRequestException invalid => (
                StatusCodes.Status422UnprocessableEntity,
                invalid.Code,
                ErrorTitles.InvalidValue,
                exception.Message,
                invalid.Pointer),

            // The one exception whose own message reaches the caller. It says which service is
            // missing and what is needed of it, which is exactly what somebody hitting a 501
            // needs to know and cannot find out anywhere else. Safe to disclose: the text is
            // written by the author of the breadcrumb, not assembled from anything the request
            // carried.
            PendingServiceException pending => (
                StatusCodes.Status501NotImplemented,
                ErrorCodes.NotImplemented,
                ErrorTitles.NotImplemented,
                pending.Message,
                null),

            // A dependency that is down, unreachable or too slow, raised by the SDK's clients. Its
            // detail names the service and nothing of how it is reached (it was written from the
            // service's name, not from anything the failure carried), so it is safe to hand on,
            // and it is what the caller needs to know which outage they are looking at.
            ServiceUnavailableException unavailable => (
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.ServiceUnavailable,
                ErrorTitles.ServiceUnavailable,
                unavailable.Detail,
                null),

            // A request the server refused while reading it: a body over the size limit (413), or
            // one the client stopped sending part way (400). The server already chose the status;
            // the caller gets it with the standard error document, as any other refused request.
            BadHttpRequestException refused => (
                refused.StatusCode,
                ErrorCodes.InvalidRequest,
                ErrorTitles.InvalidRequest,
                ErrorDetails.InvalidRequest,
                null),

            NotImplementedException => (
                StatusCodes.Status501NotImplemented,
                ErrorCodes.NotImplemented,
                ErrorTitles.NotImplemented,
                ErrorDetails.NotImplemented,
                null),

            _ => (
                StatusCodes.Status500InternalServerError,
                ErrorCodes.InternalError,
                ErrorTitles.InternalError,
                ErrorDetails.InternalError,
                null),
        };

    /// <summary>
    /// Logs at the severity the outcome deserves.
    /// </summary>
    /// <remarks>
    /// A <c>404</c> is an ordinary answer and logging it as an error would bury the ones that
    /// matter. A request the server refused while reading it is the caller's mistake too. A stub
    /// being reached is worth noticing but is not a fault. Anything unrecognised is.
    /// </remarks>
    private void Log(int status, Exception exception, string errorId, HttpContext httpContext)
    {
        var level = status switch
        {
            _ when exception is BadHttpRequestException && status < StatusCodes.Status500InternalServerError
                => LogLevel.Debug,
            StatusCodes.Status401Unauthorized => LogLevel.Debug,
            StatusCodes.Status403Forbidden => LogLevel.Debug,
            StatusCodes.Status404NotFound => LogLevel.Debug,
            StatusCodes.Status409Conflict => LogLevel.Debug,
            StatusCodes.Status422UnprocessableEntity => LogLevel.Debug,
            StatusCodes.Status501NotImplemented => LogLevel.Warning,
            _ => LogLevel.Error,
        };

        logger.Log(
            level,
            exception,
            "{Status} for {Method} {Path} ({ErrorId}).",
            status,
            httpContext.Request.Method,
            httpContext.Request.Path,
            errorId);
    }
}
