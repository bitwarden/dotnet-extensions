namespace Bitwarden.Server.Sdk.Environment;

/// <summary>
/// Provides access to the current application's version information.
/// </summary>
public interface IVersionInfoAccessor
{
    /// <summary>
    /// Gets the version information for the current application, or <see langword="null"/> if
    /// version information is unavailable.
    /// </summary>
    VersionInfo? Get();
}
