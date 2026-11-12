using System.Text.RegularExpressions;

namespace Bitwarden.Server.Sdk.Sagas;

/// <summary>
/// Tells a write-method request that only reads from one that writes, by its address: an action named <c>check</c>,
/// <c>find</c>, <c>validate</c>, <c>look-up</c>, <c>resolve</c>, <c>get</c>, <c>filter</c> or <c>holds</c>, alone or followed by
/// more words (<c>.../actions/check-plan</c>, <c>.../actions/find-by-identifier</c>), a collection of previews
/// (<c>/api/v1/invoice-previews</c>), or a request asked only to be checked (every <c>validateOnly</c> in the query is
/// <c>true</c>).
/// </summary>
/// <remarks>
/// <para>
/// The rule a service names its reading <c>POST</c> operations by; a client can use it to leave such a call out of an
/// operation's writes (<see cref="ServiceWrites"/>). A trailing <c>/</c> is ignored.
/// </para>
/// <para>
/// The verb names the read, and the rule trusts the name: an operation that writes but is named with a reading verb
/// (<c>.../actions/resolve-and-refund</c>) counts as a read. Name a write with a verb that says it writes.
/// </para>
/// </remarks>
public static partial class ReadingRoutes
{
    /// <summary>
    /// Whether a route or a path says its operation only reads.
    /// </summary>
    /// <param name="path">
    /// The route or the request's path, without its query. Prefer the route: the rule reads the last segment, which in
    /// a request's path may be a value the caller chose.
    /// </param>
    public static bool Reads(string? path) => ReadingRoute().IsMatch(path?.TrimEnd('/') ?? string.Empty);

    /// <summary>
    /// Whether a request to <paramref name="address"/> only reads: its path says so, or it asks only to be checked.
    /// </summary>
    /// <param name="address">The request's address, absolute or relative.</param>
    public static bool Reads(Uri? address)
    {
        if (address is null)
        {
            return false;
        }

        var text = address.IsAbsoluteUri ? address.PathAndQuery : address.OriginalString;
        var fragment = text.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
        {
            // PathAndQuery leaves the fragment out; a relative address keeps it, so it is left out here too.
            text = text[..fragment];
        }

        var query = text.IndexOf('?', StringComparison.Ordinal);
        var path = query < 0 ? text : text[..query];

        return Reads(path)
            || (query >= 0 && AsksOnlyToBeChecked(text[(query + 1)..]));
    }

    // Every validateOnly in the query must be true: a callee that binds the first value would write on
    // validateOnly=false&validateOnly=true.
    private static bool AsksOnlyToBeChecked(string query)
    {
        var asked = false;

        foreach (var parameter in query.Split('&'))
        {
            // A bare validateOnly, with no value, is one too, and not a true one.
            if (!parameter.StartsWith("validateOnly=", StringComparison.OrdinalIgnoreCase)
                && !parameter.Equals("validateOnly", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!parameter.Equals("validateOnly=true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            asked = true;
        }

        return asked;
    }

    // Two rules, each matched at the end of the path:
    // - a collection of previews: the last segment ends in "-previews";
    // - an action that reads: an "actions" segment, then a reading verb as the last segment, alone or followed by "-"
    //   and more words ("check", "check-plan").
    [GeneratedRegex("(/actions/(check|find|validate|look-up|resolve|get|filter|holds)(-[a-z0-9-]+)?|-previews)\\z")]
    private static partial Regex ReadingRoute();
}
