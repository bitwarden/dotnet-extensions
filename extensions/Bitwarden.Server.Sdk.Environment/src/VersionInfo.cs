using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Bitwarden.Server.Sdk.Environment;

/// <summary>
/// Represents parsed version information for a Bitwarden server application.
/// </summary>
public sealed partial class VersionInfo : ISpanParsable<VersionInfo>
{
    [GeneratedRegex("^[0-9a-f]{5,40}$")]
    private static partial Regex GitHashRegex();

    private VersionInfo(Version version, string? gitHash)
    {
        Version = version;
        GitHash = gitHash;
    }

    /// <summary>Gets the application version.</summary>
    public Version Version { get; }

    /// <summary>Gets the Git commit hash embedded in the informational version, or <see langword="null"/> if not present.</summary>
    public string? GitHash { get; }

    /// <inheritdoc />
    public static VersionInfo Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out var result))
        {
            throw new FormatException();
        }

        return result;
    }

    /// <inheritdoc />
    public static VersionInfo Parse(string? s, IFormatProvider? provider)
        => Parse(s.AsSpan(), provider);

    /// <inheritdoc />
    public static bool TryParse(
        ReadOnlySpan<char> s,
        IFormatProvider? provider,
        [MaybeNullWhen(returnValue: false)] out VersionInfo result)
    {
        result = null;
        var plusIndex = s.IndexOf('+');

        var versionPart = plusIndex == -1 ? s : s[0..plusIndex];

        // Strip SemVer pre-release label (e.g., "-alpha.1") before parsing as System.Version
        var dashIndex = versionPart.IndexOf('-');
        if (dashIndex != -1)
        {
            versionPart = versionPart[0..dashIndex];
        }

        if (!Version.TryParse(versionPart, out var version))
        {
            return false;
        }

        if (plusIndex == -1)
        {
            result = new VersionInfo(version, null);
            return true;
        }

        var gitHash = s[(plusIndex + 1)..];

        if (!GitHashRegex().IsMatch(gitHash))
        {
            return false;
        }

        result = new VersionInfo(version, gitHash.ToString());
        return true;
    }

    /// <inheritdoc />
    public static bool TryParse(string? s, IFormatProvider? provider, [MaybeNullWhen(returnValue: false)] out VersionInfo result)
        => TryParse(s.AsSpan(), provider, out result);
}
