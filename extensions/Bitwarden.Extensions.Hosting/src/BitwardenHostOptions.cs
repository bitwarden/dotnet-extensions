namespace Bitwarden.Extensions.Hosting;

/// <summary>
/// Options for configuring the Bitwarden host.
/// </summary>
public class BitwardenHostOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to include request logging.
    /// </summary>
    public bool IncludeLogging { get; set; } = true;
    /// <summary>
    /// Gets or sets a value indicating whether to include metrics.
    /// </summary>
    public bool IncludeMetrics { get; set; } = true;
}
