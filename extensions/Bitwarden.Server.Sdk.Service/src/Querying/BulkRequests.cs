using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// What every bulk operation checks of the request itself, before any resource is touched.
/// </summary>
/// <remarks>
/// The standards' request-level rules: too many items, or the same resource named twice, reject the
/// whole request in either atomicity mode; they are not per-resource failures. The limits are each
/// operation's to choose and document; these only enforce the one it chose.
/// </remarks>
public static class BulkRequests
{
    /// <summary>
    /// Refuses a request naming more than an operation's documented maximum.
    /// </summary>
    /// <param name="count">How many it names.</param>
    /// <param name="maximum">How many the operation allows.</param>
    /// <param name="pointer">The member that names them: <c>/ids</c> or <c>/data</c>.</param>
    /// <exception cref="InvalidRequestException">It names too many.</exception>
    public static void RequireAtMost(int count, int maximum, string pointer)
    {
        if (count > maximum)
        {
            throw new InvalidRequestException(
                $"{count:N0} were named and this operation takes at most {maximum:N0} at a time.",
                pointer);
        }
    }

    /// <summary>
    /// Refuses a list of identifiers naming any resource twice.
    /// </summary>
    /// <param name="ids">The identifiers, as the caller sent them.</param>
    /// <exception cref="InvalidRequestException">One appears twice; pointed at its second appearance.</exception>
    /// <remarks>
    /// Compared as identifiers rather than as text, so the same resource written in two cases is
    /// still the same resource.
    /// </remarks>
    public static void RequireDistinct(IReadOnlyList<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < ids.Count; index++)
        {
            if (!seen.Add(ids[index]))
            {
                throw new InvalidRequestException(
                    "The same item is named more than once.", $"/ids/{index}");
            }
        }
    }
}
