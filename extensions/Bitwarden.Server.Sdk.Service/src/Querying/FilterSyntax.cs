using System.Text;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Reads the standards' filter notation: comma-delimited values with backslash escapes, and the
/// bracketed form of a range.
/// </summary>
/// <remarks>
/// One reader for both, so that a comma means the same thing whether it separates the values of
/// a list or the two ends of a range, and <c>\,</c> is a comma inside a value in either.
/// </remarks>
internal static class FilterSyntax
{
    /// <summary>
    /// Splits on commas that are not escaped, unescaping <c>\,</c> and <c>\\</c> in what remains.
    /// </summary>
    /// <param name="value">The parameter's value.</param>
    /// <param name="parts">The values, trimmed.</param>
    /// <returns>False when the value ends in a lone backslash, which escapes nothing.</returns>
    public static bool TrySplit(string value, out List<string> parts)
    {
        parts = [];
        var current = new StringBuilder();

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '\\')
            {
                if (i + 1 == value.Length)
                {
                    return false;
                }

                // Whatever follows is literal. The standards name two escapes, and treating any
                // escaped character as itself is the reading that cannot surprise anybody.
                current.Append(value[++i]);
            }
            else if (c == ',')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString().Trim());

        return true;
    }

    /// <summary>
    /// Reads <c>[begin,end)</c> and its variants into bounds and inclusivity.
    /// </summary>
    /// <param name="value">The parameter's value.</param>
    /// <param name="range">The two bounds as text (null for <c>*</c>) and whether each is inclusive.</param>
    /// <returns>False when the value is not a range.</returns>
    public static bool TryRange(
        string value,
        out (string? Lower, bool LowerInclusive, string? Upper, bool UpperInclusive) range)
    {
        range = default;
        var trimmed = value.Trim();

        if (trimmed.Length < 2
            || trimmed[0] is not ('[' or '(')
            || trimmed[^1] is not (']' or ')')
            || !TrySplit(trimmed[1..^1], out var bounds)
            || bounds.Count != 2
            || bounds.Any(bound => bound.Length == 0))
        {
            return false;
        }

        range = (
            bounds[0] == "*" ? null : bounds[0],
            trimmed[0] == '[',
            bounds[1] == "*" ? null : bounds[1],
            trimmed[^1] == ']');

        return true;
    }
}
