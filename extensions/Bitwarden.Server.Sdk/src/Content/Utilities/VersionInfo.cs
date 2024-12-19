#nullable enable

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Bitwarden.Server.Sdk.Utilities.Internal;

namespace Bitwarden.Server.Sdk.Utilities;


[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete(InternalConstants.InternalMessage, DiagnosticId = InternalConstants.InternalId)]
internal sealed partial class VersionInfo : ISpanParsable<VersionInfo>
{
    [GeneratedRegex("[0-9a-f]{5,40}")]
    private static partial Regex GitHashRegex();

    private VersionInfo(Version version, string? gitHash)
    {
        Version = version;
        GitHash = gitHash;
    }

    public Version Version { get; }
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

        if (plusIndex == -1)
        {
            // No split char, treat it as version only
            if (!Version.TryParse(s, out var versionOnly))
            {
                return false;
            }

            result = new VersionInfo(versionOnly, null);
            return true;
        }

        if (!Version.TryParse(s[0..plusIndex], out var version))
        {
            return false;
        }

        var gitHash = s[++plusIndex..];

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
