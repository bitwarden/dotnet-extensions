using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Licensing;

internal sealed class ConfigureLicensingOptions : IConfigureOptions<LicensingOptions>
{
    private readonly IConfiguration _configuration;

    public ConfigureLicensingOptions(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    public void Configure(LicensingOptions options)
    {
        _configuration.GetSection("Licensing").Bind(options);
    }
}
