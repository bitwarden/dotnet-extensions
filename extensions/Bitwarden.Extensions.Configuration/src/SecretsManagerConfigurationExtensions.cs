using Bitwarden.Core;
using Bitwarden.Extensions.Configuration;

namespace Microsoft.Extensions.Configuration;

/// <summary>
/// Extension methods for adding Bitwarden Secrets Manager as a configuration source.
/// </summary>
public static class SecretsManagerConfigurationExtensions
{
    /// <summary>
    /// Adds Bitwarden Secrets Manager as a configuration source.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder to add the source to.</param>
    /// <param name="projectId">The Secrets Manager project ID.</param>
    /// <param name="accessToken">The access token for authentication.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/> for chaining.</returns>
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

    /// <summary>
    /// Adds Bitwarden Secrets Manager as a configuration source with automatic reloading.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder to add the source to.</param>
    /// <param name="projectId">The Secrets Manager project ID.</param>
    /// <param name="accessToken">The access token for authentication.</param>
    /// <param name="reloadInterval">The interval to reload secrets. If <c>null</c>, secrets are loaded once.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/> for chaining.</returns>
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

    /// <summary>
    /// Adds Bitwarden Secrets Manager as a configuration source for a specific environment.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder to add the source to.</param>
    /// <param name="projectId">The Secrets Manager project ID.</param>
    /// <param name="accessToken">The access token for authentication.</param>
    /// <param name="environment">The Bitwarden environment to use.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/> for chaining.</returns>
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

    /// <summary>
    /// Adds Bitwarden Secrets Manager as a configuration source using custom options.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder to add the source to.</param>
    /// <param name="options">The Secrets Manager configuration options.</param>
    /// <returns>The <see cref="IConfigurationBuilder"/> for chaining.</returns>
    public static IConfigurationBuilder AddSecretsManager(
        this IConfigurationBuilder configurationBuilder,
        SecretsManagerConfigurationOptions options)
    {
        return configurationBuilder.Add(new SecretsManagerConfigurationSource(options));
    }
}
