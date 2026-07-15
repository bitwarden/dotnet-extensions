namespace Bitwarden.Server.Sdk.Environment;

/// <summary>
/// Provides runtime environment information for the current Bitwarden service instance.
/// </summary>
public interface IBitwardenEnvironment
{
    /// <summary>
    /// The version of the application.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// The hash of the commit the build of this application is based on.
    /// </summary>
    public string? GitHash { get; }

    /// <summary>
    /// Indicates whether this instance of the application is being self-hosted.
    /// </summary>
    public bool SelfHosted { get; }

    /// <summary>
    /// The flavor of self-host currently being used.
    /// </summary>
    public string? SelfHostFlavor { get; }
}
