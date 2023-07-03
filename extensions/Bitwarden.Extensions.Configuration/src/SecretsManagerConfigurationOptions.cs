using System.Diagnostics.CodeAnalysis;

using Bitwarden.Core;

namespace Bitwarden.Extensions.Configuration;

public class SecretsManagerConfigurationOptions
{
    public BitwardenEnvironment Environment { get; set; } = BitwardenEnvironment.CloudUS;
    public Guid ProjectId { get; set; }
    public TimeSpan? ReloadInterval { get; set; }
    [DisallowNull]
    public AccessToken? AccessToken { get; set; }
}
