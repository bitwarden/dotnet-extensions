namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// One row of a baseline: a site that uses the restricted type in a given shape.
/// </summary>
/// <param name="Kind">The access shape.</param>
/// <param name="Member">Documentation-comment id of the restricted member; set only for member uses.</param>
/// <param name="Project">Assembly name of the compilation the use was seen in.</param>
/// <param name="Site">Documentation-comment id of the member that contains the use.</param>
/// <param name="Count">How many uses this row covers.</param>
/// <param name="Tracked">
/// Whether the rule governing the use allows new ones, in which case the row is a snapshot for
/// reporting and <c>BudgetRatchet.FindGrowth</c> exempts it from the shrink-only comparison.
/// </param>
public sealed record BudgetEntry(DependencyUsageType Kind, string? Member, string Project, string Site, int Count, bool Tracked = false)
{
    /// <summary>
    /// Orders rows by kind, member, project, then site so that serialization is deterministic.
    /// The rule belongs to the (type, member) pair rather than to a row, so
    /// <see cref="Tracked"/> is not part of a row's identity.
    /// </summary>
    /// <param name="left">The first row.</param>
    /// <param name="right">The second row.</param>
    /// <returns>A negative number, zero or a positive number, as <see cref="Comparison{T}"/> requires.</returns>
    internal static int Compare(BudgetEntry left, BudgetEntry right)
    {
        var result = left.Kind.CompareTo(right.Kind);
        if (result != 0)
        {
            return result;
        }

        result = string.CompareOrdinal(left.Member ?? string.Empty, right.Member ?? string.Empty);
        if (result != 0)
        {
            return result;
        }

        result = string.CompareOrdinal(left.Project, right.Project);
        if (result != 0)
        {
            return result;
        }

        return string.CompareOrdinal(left.Site, right.Site);
    }
}
