using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Refuses a request to a product service that carries any of the caller-context headers.
/// </summary>
/// <remarks>
/// <para>
/// A product service holds people's sessions and takes whom a request concerns from the session it
/// validated: the person, the organization and the provider are signed claims, and
/// where the request came from is its connection. The four headers
/// (<see cref="BitwardenHeaders.All"/>) are how one service tells another those things, and a
/// person has no business asserting them: a browser sends its token and Bitwarden's standard
/// client headers, nothing else.
/// </para>
/// <para>
/// Refused rather than ignored. A caller sending one has misunderstood which kind of service this
/// is, and silently answering for the session instead of what they named would hide the mistake
/// until it mattered. Refused before authentication, as one list, so the answer does not depend on
/// whether the token was any good: the headers are wrong here whoever sends them.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
internal sealed class CallerContextRefusal(RequestDelegate next)
{
    /// <summary>
    /// What the caller is told, for the header named.
    /// </summary>
    internal static string Detail(string header) =>
        $"The '{header}' header is not accepted by this service. It is a product service, called by "
        + "people, so whom a request concerns is what the session token names.";

    public async Task InvokeAsync(HttpContext http)
    {
        var refused = BitwardenHeaders.All.FirstOrDefault(header => http.Request.Headers.ContainsKey(header));

        if (refused is null)
        {
            await next(http);
            return;
        }

        await CallerContextHeaders.RefuseAsync(http, refused, Detail(refused));
    }
}
