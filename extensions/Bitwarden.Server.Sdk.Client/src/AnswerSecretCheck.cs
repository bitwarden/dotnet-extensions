namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Reads the answer secret as the host starts, so a deployment that requires one and lacks it
/// fails to start (<see cref="Resources.Caching.OwnedAnswerKey.ReadSecret(Microsoft.Extensions.Configuration.IConfiguration, string)"/>).
/// </summary>
internal sealed class AnswerSecretCheck
{
    /// <summary>
    /// The secret read, empty for none.
    /// </summary>
    public byte[] Secret { get; set; } = [];
}
