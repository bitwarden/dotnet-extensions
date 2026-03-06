using Microsoft.Extensions.Caching.StackExchangeRedis;

namespace Bitwarden.Server.Sdk.Caching;

internal static class RedisCacheOptionsExtensions
{
    public static bool IsSetup(this RedisCacheOptions options)
    {
        // TODO: Should we check any other properties?
        return !string.IsNullOrEmpty(options.Configuration);
    }
}
