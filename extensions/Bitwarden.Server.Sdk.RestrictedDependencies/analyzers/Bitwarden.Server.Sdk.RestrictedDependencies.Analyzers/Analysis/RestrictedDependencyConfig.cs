using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// What the build makes visible to the analyzer. Only projects that set
/// RestrictedDependencyAnalysis are analyzed; everything else is bypassed before any symbol is
/// looked at.
/// </summary>
internal static class RestrictedDependencyConfig
{
    public static bool IsEnabled(AnalyzerConfigOptions options) => IsTrue(options, AnalyzerConfigConstants.Analysis);

    public static bool IsBaseline(AnalyzerConfigOptions options) => IsTrue(options, AnalyzerConfigConstants.BaselineMetadata);

    public static string? GetRepoRoot(AnalyzerConfigOptions options) => GetNonEmpty(options, AnalyzerConfigConstants.RepositoryRoot);

    /// <summary>
    /// The restricted types the host named, or null when nothing did — which means the compiler is
    /// hosting and the committed baselines are the authority.
    /// </summary>
    public static ImmutableArray<string>? GetSeedTypes(AnalyzerConfigOptions options)
    {
        var value = GetNonEmpty(options, AnalyzerConfigConstants.SeedTypes);
        return value is null
            ? null
            : [.. value.Split(AnalyzerConfigConstants.SeedTypeSeparator).Select(name => name.Trim()).Where(name => name.Length > 0)];
    }

    /// <summary>
    /// How a diagnostic tells the reader to rebuild the baselines. The command belongs to the
    /// consuming repository, so it is configured rather than hardcoded.
    /// </summary>
    public static string GetUpdateInstruction(AnalyzerConfigOptions options)
    {
        var command = GetNonEmpty(options, AnalyzerConfigConstants.UpdateCommand);
        return command is null ? "regenerate the committed baselines" : $"run `{command}`";
    }

    private static bool IsTrue(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string? GetNonEmpty(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
