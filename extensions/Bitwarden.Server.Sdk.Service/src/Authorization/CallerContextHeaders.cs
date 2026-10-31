using System.Net.Mime;
using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Reads a domain service's caller context from the call's signed context token, once per request.
/// </summary>
/// <remarks>
/// <para>
/// After authentication, because only an internal service token's caller is believed
/// (<see cref="InternalCaller"/>); from anybody else the headers are ignored and the context is empty.
/// From a service, a call carrying caller context must carry one <see cref="BitwardenHeaders.Delegation"/>:
/// a context token Identity signed for this service and that caller (<see cref="CallerDelegation"/>).
/// Missing beside a plain header, forged, expired, addressed elsewhere or issued to another caller, and
/// the call is refused <c>401</c> before the operation runs; a call carrying no caller context at all
/// has an empty context. Its context is then the token's: a malformed
/// <see cref="BitwardenHeaders.Context"/> object in it, or a plain header that disagrees with it, is
/// refused <c>400</c>, so nothing reading the plain headers can be told something else.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
internal sealed class CallerContextHeaders(RequestDelegate next)
{
    private static readonly object Key = new();

    public async Task InvokeAsync(HttpContext http)
    {
        if (InternalCaller.Is(http.User))
        {
            var verified = await CallerDelegation.VerifyAsync(http);

            if (verified.Refusal is { } refusal)
            {
                await RefuseAsync(http, refusal.Header, refusal.Detail, refusal.Status);
                return;
            }

            if (verified.Context is { } context)
            {
                if (AssertsAPersonUnaccepted(http, context))
                {
                    http.RequestServices.GetRequiredService<ILogger<CallerContextHeaders>>().LogWarning(
                        "Refused an asserted caller context naming a person: {Operation} accepts only a delegated one. Called by {Client}.",
                        http.GetEndpoint()?.DisplayName,
                        http.User.FindFirst(InternalCaller.ClientClaim)?.Value);

                    await RefuseAsync(
                        http,
                        BitwardenHeaders.Delegation,
                        "This operation acts for a person only on their own request: the call's context must be delegated from "
                        + "their token, not asserted by the calling service.",
                        StatusCodes.Status403Forbidden);
                    return;
                }

                Keep(http, context);
            }
        }

        await next(http);
    }

    /// <summary>
    /// Whether a call's context is a service's assertion naming a person, at an operation that doesn't accept one from
    /// this caller (<see cref="AcceptsAssertedContextAttribute"/>).
    /// </summary>
    private static bool AssertsAPersonUnaccepted(HttpContext http, VerifiedContext context)
    {
        // A further hop of an asserted context was admitted where it was first asserted: a service holding only its own
        // credential can have nothing but a first hop signed, its own token as the subject.
        if (context.Mode != CallerContextToken.Asserted || context.User is null || context.Hops > 1)
        {
            return false;
        }

        var accepted = http.GetEndpoint()?.Metadata.GetMetadata<AcceptsAssertedContextAttribute>();
        return accepted is null || !accepted.Admits(http.User.FindFirst(InternalCaller.ClientClaim)?.Value);
    }

    /// <summary>
    /// Keeps a call's verified context for the rest of the request.
    /// </summary>
    internal static void Keep(HttpContext http, VerifiedContext verified) => http.Items[Key] = verified;

    /// <summary>
    /// The verified context of a call from another service, or null when there is none.
    /// </summary>
    internal static VerifiedContext? Verified(HttpContext http) =>
        http.Items.TryGetValue(Key, out var verified) ? verified as VerifiedContext : null;

    /// <summary>
    /// What <see cref="BitwardenHeaders.Context"/> carried, as the context token signed it.
    /// </summary>
    internal static CallerContextValue? Read(HttpContext http) => Verified(http)?.Context;

    /// <summary>
    /// Answers <c>400</c> (or the status given) in the standard error shape, pointing at the header.
    /// </summary>
    internal static async Task RefuseAsync(HttpContext http, string header, string detail, int status = StatusCodes.Status400BadRequest)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = MediaTypeNames.Application.Json;

        await http.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Errors =
            [
                new Error
                {
                    Status = status.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Code = status switch
                    {
                        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
                        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
                        _ => ErrorCodes.InvalidRequest,
                    },
                    Title = status switch
                    {
                        StatusCodes.Status401Unauthorized => ErrorTitles.Unauthenticated,
                        StatusCodes.Status403Forbidden => ErrorTitles.Forbidden,
                        _ => ErrorTitles.InvalidRequest,
                    },
                    Detail = detail,
                    Source = new ErrorSource { Header = header },
                },
            ],
        });
    }
}
