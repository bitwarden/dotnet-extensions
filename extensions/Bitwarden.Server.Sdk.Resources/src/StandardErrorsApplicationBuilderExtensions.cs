using Microsoft.AspNetCore.Builder;

namespace Bitwarden.Server.Sdk.Resources;

/// <summary>
/// Answers a failure that set a status and wrote no body with the standard error document.
/// </summary>
public static class StandardErrorsApplicationBuilderExtensions
{
    /// <summary>
    /// Writes the standard error document for a response that has a failure status and no body, such as a <c>404</c>
    /// from routing or a <c>405</c> from a method mismatch, rather than a problem document no operation advertises.
    /// </summary>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseStandardStatusCodePages(this IApplicationBuilder app) =>
        app.UseStatusCodePages(StatusCodeErrorWriter.Write);
}
