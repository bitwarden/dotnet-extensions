using Microsoft.AspNetCore.Builder;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Adds idempotency keys to the pipeline.
/// </summary>
public static class IdempotencyApplicationBuilderExtensions
{
    /// <summary>
    /// Does a write another service sends with an <c>Idempotency-Key</c> once, and answers a repeat with the first
    /// answer.
    /// </summary>
    /// <remarks>
    /// Add it last, so the caller is authenticated and authorized and the operation chosen. A refusal before it is
    /// never recorded.
    /// </remarks>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseIdempotencyKeys(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotentWrites>();
}
