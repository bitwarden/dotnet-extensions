using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Features;

/// <summary>
/// A context class for when a feature check has failed.
/// </summary>
public class FeatureCheckFailedContext
{
    /// <summary>
    /// The <see cref="IFeatureMetadata"/> that failed their check and is the reason
    /// <see cref="FeatureCheckOptions.OnFeatureCheckFailed"/> was called.
    /// </summary>
    public required IFeatureMetadata FailedMetadata { get; init; }

    /// <summary>
    /// The <see cref="HttpContext"/> of the current request.
    /// </summary>
    public required HttpContext HttpContext { get; init; }
}
