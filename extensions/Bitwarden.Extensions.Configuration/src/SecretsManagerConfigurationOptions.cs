using System.Diagnostics.CodeAnalysis;

using Bitwarden.Core;

namespace Bitwarden.Extensions.Configuration;

/// <summary>
/// Configuration options for integrating Bitwarden Secrets Manager as a configuration source.
/// </summary>
public class SecretsManagerConfigurationOptions
{
    /// <summary>
    /// Gets or sets the Bitwarden environment to connect to. Defaults to <see cref="BitwardenEnvironment.CloudUS"/>.
    /// </summary>
    public BitwardenEnvironment Environment { get; set; } = BitwardenEnvironment.CloudUS;

    /// <summary>
    /// Gets or sets the project ID from which to load secrets.
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// Gets or sets the interval for automatically reloading secrets. If <c>null</c>, secrets are loaded once and not reloaded.
    /// </summary>
    public TimeSpan? ReloadInterval { get; set; }

    /// <summary>
    /// Gets or sets the access token for authenticating with Secrets Manager.
    /// </summary>
    [DisallowNull]
    public AccessToken? AccessToken { get; set; }
}
