using Bitwarden.Extensions.Hosting;

namespace Bitwarden.Extensions.WebHosting;

public class BitwardenWebHostOptions : BitwardenHostOptions
{
    public bool IncludeRequestLogging { get; set; }
}
