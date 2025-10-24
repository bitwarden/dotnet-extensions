using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Features;

internal sealed class FeatureCheckMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FeatureCheckMiddleware> _logger;
    private readonly FeatureCheckOptions _featureCheckOptions;

    public FeatureCheckMiddleware(
        RequestDelegate next,
        ILogger<FeatureCheckMiddleware> logger,
        IOptions<FeatureCheckOptions> featureCheckOptions)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(featureCheckOptions);

        _next = next;
        _logger = logger;
        _featureCheckOptions = featureCheckOptions.Value;
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
            if (featureMetadata.FeatureCheck(featureService))
            {
                continue;
            }

            var failedContext = new FeatureCheckFailedContext
            {
                FailedMetadata = featureMetadata,
                HttpContext = context,
            };
            // Do not execute more of the pipeline, return early.
            return _featureCheckOptions.OnFeatureCheckFailed(failedContext);
        }

        // Either there were no feature checks, or none were failed. Continue on in the pipeline.
        return _next(context);
    }
}
