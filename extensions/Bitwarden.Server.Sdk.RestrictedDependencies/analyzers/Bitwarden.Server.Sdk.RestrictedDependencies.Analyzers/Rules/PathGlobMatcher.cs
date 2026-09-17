using System.Text;
using System.Text.RegularExpressions;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;

/// <summary>
/// A CODEOWNERS-style glob over repo-relative, forward-slash paths. <c>**</c> spans directories,
/// <c>*</c> and <c>?</c> stay inside one segment.
/// </summary>
internal sealed class PathGlobMatcher
{
    private readonly Regex _regex;

    private PathGlobMatcher(Regex regex)
    {
        _regex = regex;
    }

    /// <summary>
    /// Compiles <paramref name="pattern"/>. Returns false for an empty pattern, a backslash, a
    /// leading slash or an empty segment, all of which indicate a path that was not written in
    /// repo-relative form.
    /// </summary>
    public static bool TryCreate(string? pattern, out PathGlobMatcher? glob)
    {
        glob = null;
        if (pattern is null || string.IsNullOrWhiteSpace(pattern) || pattern.IndexOf('\\') >= 0
            || pattern.StartsWith("/", StringComparison.Ordinal) || pattern.Contains("//"))
        {
            return false;
        }

        var builder = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 2 < pattern.Length && pattern[i + 1] == '*' && pattern[i + 2] == '/')
            {
                builder.Append("(?:.*/)?");
                i += 2;
            }
            else if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                builder.Append(".*");
                i++;
            }
            else if (c == '*')
            {
                builder.Append("[^/]*");
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        builder.Append('$');
        glob = new PathGlobMatcher(new Regex(builder.ToString(), RegexOptions.CultureInvariant));
        return true;
    }

    /// <summary>
    /// True when the repo-relative, forward-slash <paramref name="relativePath"/> matches.
    /// </summary>
    public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath);
}
