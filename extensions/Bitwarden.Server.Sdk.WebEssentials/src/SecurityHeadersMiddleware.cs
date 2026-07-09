using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bitwarden.Server.Sdk.WebEssentials;

internal sealed class SecurityHeadersMiddleware
{
    private static readonly StringValues _frameOptionsHeaderValue = new("SAMEORIGIN");
    private static readonly StringValues _xssProtectionHeaderValue = new("1; mode=block");
    private static readonly StringValues _contentTypeOptionsHeaderValue = new("nosniff");

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);

        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Frame-Options
        context.Response.Headers.Append("x-frame-options", _frameOptionsHeaderValue);

        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-XSS-Protection
        context.Response.Headers.Append("x-xss-protection", _xssProtectionHeaderValue);

        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/X-Content-Type-Options
        context.Response.Headers.Append("x-content-type-options", _contentTypeOptionsHeaderValue);

        return _next(context);
    }
}
