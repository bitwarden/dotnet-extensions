using Bitwarden.Extensions.Hosting;

namespace Bitwarden.Extensions.WebHosting;

/// <summary>
/// Options for configuring the web host.
/// </summary>
public class BitwardenWebHostOptions : BitwardenHostOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to include request logging.
    /// </summary>
    public bool IncludeRequestLogging { get; set; }
}
