using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// What a bulk operation did.
/// </summary>
/// <remarks>
/// A count and nothing else. Returning the resources would make the response unbounded by
/// anything the caller chose, which is the problem paging exists to solve, and a caller who
/// wants to know which rows matched can ask for them with the same filter.
/// </remarks>
public sealed record BulkResult
{
    /// <summary>
    /// How many resources succeeded, and on a best-effort run how many did not.
    /// </summary>
    public required IDictionary<string, object?> Meta { get; init; }

    /// <summary>
    /// Why each failed resource failed. Best-effort only.
    /// </summary>
    /// <remarks>
    /// One error at least per resource that failed, each carrying the status the
    /// single-resource operation would have returned for it: <c>404</c> for an identifier
    /// that names nothing, <c>422</c> for a rule it broke. An all-or-nothing run has no
    /// partial outcome to report: either every resource succeeded, or the whole request
    /// failed and the response is an ordinary error document.
    /// </remarks>
    public IReadOnlyList<Error>? Errors { get; init; }

    /// <summary>
    /// States that an all-or-nothing operation affected a number of resources.
    /// </summary>
    /// <param name="affected">How many.</param>
    public static BulkResult Affected(int affected) =>
        new() { Meta = new Dictionary<string, object?> { ["affectedCount"] = affected } };

    /// <summary>
    /// States what a best-effort operation managed.
    /// </summary>
    /// <param name="affected">How many resources succeeded.</param>
    /// <param name="failures">Why each of the others did not.</param>
    /// <remarks>
    /// <c>failedCount</c> is required on a best-effort response even when it is zero. A caller
    /// that has to infer "nothing failed" from an absent member is a caller that cannot tell it
    /// from a service that forgot to say.
    /// </remarks>
    public static BulkResult BestEffort(int affected, IReadOnlyList<Error> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return new BulkResult
        {
            Meta = new Dictionary<string, object?>
            {
                ["affectedCount"] = affected,
                ["failedCount"] = failures.Count,
            },
            Errors = failures.Count == 0 ? null : failures,
        };
    }
}
