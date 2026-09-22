namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// The analyzer-config keys the analyzer reads. A build supplies them by making the matching
/// MSBuild properties compiler-visible, which the package's build props do; a tool that hosts the
/// analyzer out of band fabricates them instead.
/// </summary>
public static class AnalyzerConfigConstants
{
    /// <summary>
    /// <c>true</c> to analyze the project. Anything else is skipped before a symbol is looked at.
    /// </summary>
    public const string Analysis = "build_property.RestrictedDependencyAnalysis";

    /// <summary>
    /// Repository root, so paths in messages and <c>AllowedPaths</c> globs are repo-relative.
    /// </summary>
    public const string RepositoryRoot = "build_property.RepoRoot";

    /// <summary>
    /// The consuming repository's regenerate command, quoted back in BW0013 and BW0015.
    /// </summary>
    public const string UpdateCommand = "build_property.RestrictedDependencyUpdateCommand";

    /// <summary>
    /// Restricted types named by a hosting tool, separated by <see cref="SeedTypeSeparator"/>.
    /// Naming any also puts the analyzer in observe mode: it enforces nothing, because it cannot
    /// treat the baseline it is rebuilding as the authority. A real build never sets it.
    /// </summary>
    public const string SeedTypes = "build_property.RestrictedDependencySeedTypes";

    /// <summary>
    /// Item metadata marking an <c>AdditionalFiles</c> entry as a committed baseline. An unmarked
    /// file reports this as an empty value rather than being absent, so it must be compared
    /// against <c>"true"</c> rather than probed for presence.
    /// </summary>
    public const string BaselineMetadata = "build_metadata.AdditionalFiles.RestrictedDependencyBaseline";

    /// <summary>
    /// Separates the names in <see cref="SeedTypes"/>.
    /// </summary>
    public const char SeedTypeSeparator = ';';
}
