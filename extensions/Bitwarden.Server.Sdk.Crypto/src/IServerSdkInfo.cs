namespace Bitwarden.Server.Sdk.Crypto;

/// <summary>
/// Describes the Rust SDK build backing this package.
/// </summary>
public interface IServerSdkInfo
{
    /// <summary>
    /// The SDK version the native library was built from, e.g. <c>0.1.0</c>.
    /// </summary>
    string Version { get; }
}
