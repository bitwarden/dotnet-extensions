using System.Text.Json.Nodes;

namespace Bitwarden.Extensions.Hosting.Features;

/// <summary>
/// A collection of Launch Darkly specific options.
/// </summary>
public sealed class LaunchDarklyOptions
{
    /// <summary>
    /// The SdkKey to be used for retrieving feature flag values from Launch Darkly.
    /// </summary>
    public string? SdkKey { get; set; }
}

/// <summary>
/// A set of options for features.
/// </summary>
public sealed class FeatureFlagOptions
{
    /// <summary>
    /// All the flags known to this instance, this is used to enumerable values in <see cref="IFeatureService.GetAll()"/>.
    /// </summary>
    public HashSet<string> KnownFlags { get; set; } = new HashSet<string>();

    /// <summary>
    /// Flags and flag values to include in the feature flag data source.
    /// </summary>
    public Dictionary<string, string> FlagValues { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Launch Darkly specific options.
    /// </summary>
    public LaunchDarklyOptions LaunchDarkly { get; set; } = new LaunchDarklyOptions();
}
