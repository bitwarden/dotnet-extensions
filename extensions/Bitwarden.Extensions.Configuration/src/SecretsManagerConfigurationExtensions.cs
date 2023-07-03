using Bitwarden.Core;
using Bitwarden.Extensions.Configuration;

namespace Microsoft.Extensions.Configuration;

public static class SecretsManagerConfigurationExtensions
{
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder configurationBuilder,
        Guid projectId,
        string accessToken)
    {
        return configurationBuilder.AddSecretsManager(new SecretsManagerConfigurationOptions
        {
            ProjectId = projectId,
            AccessToken = AccessToken.Parse(accessToken),
        });
    }

    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder configurationBuilder,
        Guid projectId,
        string accessToken,
        TimeSpan? reloadInterval)
    {
        return configurationBuilder.AddSecretsManager(new SecretsManagerConfigurationOptions
        {
            ProjectId = projectId,
            AccessToken = AccessToken.Parse(accessToken),
            ReloadInterval = reloadInterval,
        });
    }

    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder configurationBuilder,
        Guid projectId,
        string accessToken,
        BitwardenEnvironment environment)
    {
        return configurationBuilder.AddSecretsManager(new SecretsManagerConfigurationOptions
        {
            ProjectId = projectId,
            AccessToken = AccessToken.Parse(accessToken),
            Environment = environment,
        });
    }

    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder configurationBuilder,
        SecretsManagerConfigurationOptions options)
    {
        return configurationBuilder.Add(new SecretsManagerConfigurationSource(options));
    }
}
