using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.Features;

/// <summary>
/// A set of options for configuring how feature checks behave.
/// </summary>
public class FeatureCheckOptions
{
    /// <summary>
    /// Invoked when a feature check on <see cref="IFeatureMetadata"/> fails. Defaults to writing failure to the
    /// response using <see cref="ProblemDetails"/> format.
    /// </summary>
    public Func<FeatureCheckFailedContext, Task> OnFeatureCheckFailed { get; set; } = DefaultFeatureCheckFailedAsync;

    private static async Task DefaultFeatureCheckFailedAsync(FeatureCheckFailedContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Bitwarden.Server.Sdk.Features.FeatureCheck");
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogFailedFeatureCheck(context.FailedMetadata.ToString()!);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;

        var problemDetails = new ProblemDetails
        {
            Title = "Resource not found.",
            Status = StatusCodes.Status404NotFound,
        };

        var hostEnvironment = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        if (hostEnvironment.IsDevelopment())
        {
            // Add extra information
            problemDetails.Detail = $"Feature check failed: {context.FailedMetadata}";
        }

        var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        // We don't really care if this fails, we will return the 404 no matter what.
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = problemDetails,
        });
    }
}
