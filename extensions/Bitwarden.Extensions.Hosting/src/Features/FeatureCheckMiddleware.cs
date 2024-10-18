using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Extensions.Hosting.Features;

internal sealed class FeatureCheckMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<FeatureCheckMiddleware> _logger;

    public FeatureCheckMiddleware(
        RequestDelegate next,
        IHostEnvironment hostEnvironment,
        IProblemDetailsService problemDetailsService,
        ILogger<FeatureCheckMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(hostEnvironment);
        ArgumentNullException.ThrowIfNull(problemDetailsService);
        ArgumentNullException.ThrowIfNull(logger);

        _next = next;
        _hostEnvironment = hostEnvironment;
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public Task Invoke(HttpContext context, IFeatureService featureService)
    {
        // This middleware is expected to be placed after `UseRouting()` which will fill in this endpoint
        var endpoint = context.GetEndpoint();

        if (endpoint == null)
        {
            _logger.LogNoEndpointWarning();
            return _next(context);
        }

        var featureMetadatas = endpoint.Metadata.GetOrderedMetadata<IFeatureMetadata>();

        foreach (var featureMetadata in featureMetadatas)
        {
            if (!featureMetadata.FeatureCheck(featureService))
            {
                // Do not execute more of the pipeline, return early.
                return HandleFailedFeatureCheck(context, featureMetadata);
            }

            // Continue checking
        }

        // Either there were no feature checks, or none were failed. Continue on in the pipeline.
        return _next(context);
    }

    private async Task HandleFailedFeatureCheck(HttpContext context, IFeatureMetadata failedFeature)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogFailedFeatureCheck(failedFeature.ToString() ?? "Unknown Check");
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;

        var problemDetails = new ProblemDetails();
        problemDetails.Title = "Resource not found.";
        problemDetails.Status = StatusCodes.Status404NotFound;

        // Message added for legacy reasons. We should start preferring title/detail
        problemDetails.Extensions["Message"] = "Resource not found.";

        // Follow ProblemDetails output type? Would need clients update
        if (_hostEnvironment.IsDevelopment())
        {
            // Add extra information
            problemDetails.Detail = $"Feature check failed: {failedFeature}";
        }

        // We don't really care if this fails, we will return the 404 no matter what.
        await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problemDetails,
            // TODO: Add metadata?
        });
    }


}
