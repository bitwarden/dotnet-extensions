using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// For a test reading a domain service's context with no pipeline: the context a request's plain
/// headers carry, kept as the verified context token would have left it.
/// </summary>
internal static class SignedHeaders
{
    /// <summary>
    /// Keeps what the headers say as the request's verified context, when the context object parses.
    /// </summary>
    public static void Keep(HttpContext http)
    {
        var headers = http.Request.Headers;

        if (!CallerContextHeader.TryParse(
                headers[BitwardenHeaders.Context].Count == 1 ? headers[BitwardenHeaders.Context][0] : """{"v":1}""",
                out var context,
                out _))
        {
            return;
        }

        CallerContextHeaders.Keep(http, new VerifiedContext(
            Owner(headers, BitwardenHeaders.Organization),
            Owner(headers, BitwardenHeaders.User),
            Owner(headers, BitwardenHeaders.Provider),
            context,
            "a-context-token",
            CallerContextToken.Asserted));
    }

    private static Guid? Owner(IHeaderDictionary headers, string name) =>
        Guid.TryParse(headers[name].FirstOrDefault(), out var owner) ? owner : null;
}
