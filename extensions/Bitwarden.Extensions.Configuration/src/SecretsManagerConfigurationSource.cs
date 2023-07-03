using Microsoft.Extensions.Configuration;

namespace Bitwarden.Extensions.Configuration;

internal class SecretsManagerConfigurationSource : IConfigurationSource
{
    private readonly SecretsManagerConfigurationOptions _options;

    public SecretsManagerConfigurationSource(SecretsManagerConfigurationOptions options)
    {
        _options = options;
    }
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new SecretsManagerConfigurationProvider(_options);
    }
}
