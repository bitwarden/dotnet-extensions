using Bitwarden.Server.Sdk.CryptoPrimitives;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Reads the answer secret as the host starts, so a deployment that requires one and lacks it
/// fails to start (<see cref="Resources.Caching.OwnedAnswerKey.ReadSecret(Microsoft.Extensions.Configuration.IConfiguration, string)"/>).
/// </summary>
internal sealed class AnswerSecretCheck
{
    /// <summary>
    /// The secret read: <see cref="CacheSecret.Development"/> when none is configured and none is required.
    /// </summary>
    public CacheSecret Secret { get; set; }
}
