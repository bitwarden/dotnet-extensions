namespace Bitwarden.Extensions.Hosting;

/// <summary>
/// Options for configuring the Bitwarden host.
/// </summary>
public class BitwardenHostOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to include request logging, defaults to true.
    /// </summary>
    public bool IncludeLogging { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether to include metrics, defaults to true.
    /// </summary>
    public bool IncludeMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating if self-hosting capabilities should be added to the service, defaults to false.
    /// </summary>
    /// <remarks>
    /// If this is not turned on, the assumption is made that the service is running in a cloud environment.
    /// </remarks>
    public bool IncludeSelfHosting { get; set; }
}
