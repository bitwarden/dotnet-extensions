namespace Bitwarden.Extensions.Hosting;

/// <summary>
/// Global settings.
/// </summary>
public class GlobalSettingsBase
{
    /// <summary>
    /// Gets or sets a value indicating whether the application is self-hosted.
    /// </summary>
    public bool IsSelfHosted { get; set; }
}
