using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Writes the body of a refusal over a limit, after the status and <c>Retry-After</c> are set. The
/// platform's writes its error document; a service whose clients expect another body registers one
/// that writes it.
/// </summary>
public interface IIpRateLimitRefusal
{
    /// <summary>
    /// Writes the refusal's body.
    /// </summary>
    /// <param name="http">The refused request, its status and <c>Retry-After</c> already set.</param>
    /// <param name="message">The sentence: "Slow down! Too many requests. Try again in {period}."</param>
    Task WriteAsync(HttpContext http, string message);
}
