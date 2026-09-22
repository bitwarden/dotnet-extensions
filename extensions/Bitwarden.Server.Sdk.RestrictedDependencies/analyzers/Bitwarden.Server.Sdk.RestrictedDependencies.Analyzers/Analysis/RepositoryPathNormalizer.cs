namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// Turns compiler file paths into the repo-relative, forward-slash form that
/// <see cref="Rules.PathGlobMatcher"/> matches, so AllowedPaths read the same on every operating
/// system.
/// </summary>
internal static class RepositoryPathNormalizer
{
    /// <summary>
    /// Makes <paramref name="filePath"/> relative to <paramref name="repoRoot"/>. When the file is
    /// not under the root, or no root is known, the normalized absolute path is returned so that no
    /// glob can accidentally match it.
    /// </summary>
    public static string ToRelative(string filePath, string? repoRoot)
    {
        var normalized = Normalize(filePath);
        if (string.IsNullOrEmpty(repoRoot))
        {
            return normalized;
        }

        var root = Normalize(repoRoot!);
        if (!root.EndsWith("/", StringComparison.Ordinal))
        {
            root += "/";
        }

        return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(root.Length)
            : normalized;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
