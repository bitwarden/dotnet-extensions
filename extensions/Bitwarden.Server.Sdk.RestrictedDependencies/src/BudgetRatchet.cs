using System.Globalization;

namespace Bitwarden.Server.Sdk.RestrictedDependencies;

/// <summary>
/// The shrink-only comparison behind a repository's baseline check. It compares two sets of
/// baseline documents purely as data: a per (type, kind, member) total that rose across projects,
/// or a sealed type whose declared-member list grew, is growth. Moves between methods or projects
/// net to zero and pass.
///
/// Rows marked <see cref="BudgetEntry.Tracked"/> are exempt. A tracked-only rule allows new uses
/// by design and the analyzer never reports one, so its total is expected to rise; comparing it
/// would fail the check for exactly the code the rule permits.
/// </summary>
public static class BudgetRatchet
{
    /// <summary>
    /// Returns one human-readable line per growth found in <paramref name="head"/> relative to
    /// <paramref name="base"/>. An empty result means the change only shrank or moved budget, or
    /// grew only where a tracked-only rule allows it.
    /// </summary>
    /// <param name="base">The baselines committed at the reference being compared against.</param>
    /// <param name="head">The baselines in the tree under review.</param>
    /// <returns>One line per growth, in type order; empty when nothing grew.</returns>
    public static IReadOnlyList<string> FindGrowth(IEnumerable<BudgetModel> @base, IEnumerable<BudgetModel> head)
    {
        var baseByType = @base.ToDictionary(b => b.Type, StringComparer.Ordinal);
        var growth = new List<string>();

        foreach (var headBudget in head.OrderBy(h => h.Type, StringComparer.Ordinal))
        {
            baseByType.TryGetValue(headBudget.Type, out var baseBudget);

            foreach (var member in headBudget.DeclaredMembers
                         .Except(baseBudget?.DeclaredMembers ?? [], StringComparer.Ordinal)
                         .OrderBy(m => m, StringComparer.Ordinal))
            {
                growth.Add($"{headBudget.Type}: sealed type gained member '{member}'.");
            }

            var baseTotals = Totals(baseBudget);
            foreach (var pair in Totals(headBudget).OrderBy(kv => kv.Key.kind).ThenBy(kv => kv.Key.member, StringComparer.Ordinal))
            {
                var key = pair.Key;
                if (pair.Value.Tracked)
                {
                    continue;
                }

                var headTotal = pair.Value.Count;
                baseTotals.TryGetValue(key, out var baseTotal);
                if (headTotal > baseTotal.Count)
                {
                    var subject = key.member is null ? key.kind.ToToken() : $"{key.kind.ToToken()} '{key.member}'";
                    growth.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}: {1} total rose from {2} to {3}.", headBudget.Type, subject, baseTotal.Count, headTotal));
                }
            }
        }

        return growth;
    }

    /// <summary>
    /// The per (kind, member) total, and whether any row contributing to it is tracked only — in
    /// which case the whole total is exempt, because the rule belongs to the (type, member) pair
    /// rather than to one row.
    /// </summary>
    private static Dictionary<(DependencyUsageType kind, string? member), (int Count, bool Tracked)> Totals(BudgetModel? budget)
    {
        var totals = new Dictionary<(DependencyUsageType, string?), (int Count, bool Tracked)>();
        if (budget is null)
        {
            return totals;
        }

        foreach (var usage in budget.Usages)
        {
            var key = (usage.Kind, usage.Member);
            totals.TryGetValue(key, out var current);
            totals[key] = (current.Count + usage.Count, current.Tracked || usage.Tracked);
        }

        return totals;
    }
}
