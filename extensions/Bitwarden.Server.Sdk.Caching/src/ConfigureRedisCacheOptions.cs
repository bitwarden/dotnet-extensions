using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static Bitwarden.Server.Sdk.Caching.ConfigHelpers;

namespace Bitwarden.Server.Sdk.Caching;

internal sealed class ConfigureRedisCacheOptions : IConfigureNamedOptions<RedisCacheOptions>
{
    private readonly IConfiguration _configuration;

    public ConfigureRedisCacheOptions(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public void Configure(string? name, RedisCacheOptions options)
    {
        if (name == null || name == Options.DefaultName)
        {
            return;
        }

        var useSection = _configuration.GetSection($"Caching:Uses:{name}:Redis");
        var defaultSection = _configuration.GetSection("Caching:Redis");

        if (TryGetDefinedValue(useSection, defaultSection, nameof(RedisCacheOptions.Configuration), out string? configuration))
        {
            options.Configuration = configuration;
        }

        if (TryGetDefinedValue(useSection, defaultSection, nameof(RedisCacheOptions.InstanceName), out string? instanceName))
        {
            options.InstanceName = instanceName;
        }
    }

    public void Configure(RedisCacheOptions options)
    {
        // Nothing to do here, the only reasonable thing would be to call Configure(Options.DefaultName, options)
        // would just return early.
    }
}
