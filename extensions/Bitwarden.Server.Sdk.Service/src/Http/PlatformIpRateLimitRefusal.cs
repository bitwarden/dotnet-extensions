using System.Globalization;
using System.Net.Mime;
using Bitwarden.Server.Sdk.Common.Errors;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// The platform's refusal body: its error document, code <see cref="IpRateLimiting.TooManyRequests"/>.
/// </summary>
internal sealed class PlatformIpRateLimitRefusal : IIpRateLimitRefusal
{
    public Task WriteAsync(HttpContext http, string message)
    {
        http.Response.ContentType = MediaTypeNames.Application.Json;

        return http.Response.WriteAsJsonAsync(
            new ErrorResponse
            {
                Errors =
                [
                    new Error
                    {
                        Status = http.Response.StatusCode.ToString(CultureInfo.InvariantCulture),
                        Code = IpRateLimiting.TooManyRequests,
                        Title = "Too many requests.",
                        Detail = message,
                    },
                ],
            },
            http.RequestAborted);
    }
}
