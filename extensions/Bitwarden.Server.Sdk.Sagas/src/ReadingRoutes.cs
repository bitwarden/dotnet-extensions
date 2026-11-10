using System.Text.RegularExpressions;

namespace Bitwarden.Server.Sdk.Sagas;

/// <summary>
/// Tells a write-method request that only reads from one that writes, by its address: an action named <c>check</c>,
/// <c>find</c>, <c>validate</c>, <c>look-up</c>, <c>resolve</c>, <c>get</c>, <c>filter</c> or <c>holds</c>, alone or followed by
/// more words (<c>.../actions/check-plan</c>, <c>.../actions/find-by-identifier</c>), a collection of previews
/// (<c>/api/v1/invoice-previews</c>), or a request asked only to be checked (<c>validateOnly=true</c> anywhere in the
/// query).
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
    /// <param name="path">The route or the request's path, without its query.</param>
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
        var query = text.IndexOf('?', StringComparison.Ordinal);
        var path = query < 0 ? text : text[..query];

        return Reads(path)
            || (query >= 0 && ValidateOnly().IsMatch(text[(query + 1)..]));
    }

    [GeneratedRegex("(/actions/(check|find|validate|look-up|resolve|get|filter|holds)(-[a-z0-9-]+)?|-previews)\\z")]
    private static partial Regex ReadingRoute();

    [GeneratedRegex("(^|&)validateOnly=true(&|\\z)", RegexOptions.IgnoreCase)]
    private static partial Regex ValidateOnly();
}
