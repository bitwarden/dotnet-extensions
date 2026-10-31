using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// The middleware: refuses a request over a limit before anything else answers it.
/// </summary>
internal sealed class IpRateLimitMiddleware(RequestDelegate next, IpRateLimiting limits)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (limits.Enabled && await limits.TryTakeAsync(http) is { } exceeded)
        {
            await limits.RefuseAsync(http, exceeded.Rule, exceeded.Period);
            return;
        }

        await next(http);
    }
}
