using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// The committed baselines this compilation can see. Read-only once constructed, so the concurrent
/// callbacks can share it freely.
/// </summary>
internal sealed class BudgetIndex
{
    private readonly Dictionary<string, BudgetModel> _budgets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    private BudgetIndex()
    {
    }

    /// <summary>
    /// Loads every AdditionalFile the build marked as a baseline. Unreadable, duplicate and
    /// malformed files are skipped and described in <paramref name="problems"/> for the caller to
    /// report as BW0015; one bad file never stops the rest from loading.
    /// </summary>
    /// <remarks>
    /// Baselines are identified by the <c>RestrictedDependencyBaseline</c> metadata the package's
    /// build props make compiler-visible, not by where they sit. The analyzer therefore knows
    /// nothing about a consuming repository's directory layout.
    /// </remarks>
    public static BudgetIndex Load(
        AnalyzerOptions options,
        string? repoRoot,
        CancellationToken cancellationToken,
        out ImmutableArray<string> problems)
    {
        var index = new BudgetIndex();
        var found = ImmutableArray.CreateBuilder<string>();

        foreach (var file in options.AdditionalFiles)
        {
            if (!RestrictedDependencyConfig.IsBaseline(options.AnalyzerConfigOptionsProvider.GetOptions(file)))
            {
                continue;
            }

            var relativePath = RepositoryPathNormalizer.ToRelative(file.Path, repoRoot);
            var text = file.GetText(cancellationToken)?.ToString();
            if (text is null)
            {
                found.Add($"Baseline '{relativePath}' could not be read.");
                continue;
            }

            try
            {
                var budget = BudgetModel.Parse(text);
                if (index._budgets.ContainsKey(budget.Type))
                {
                    found.Add($"Baseline '{relativePath}' duplicates the baseline for '{budget.Type}'.");
                    continue;
                }

                index._budgets[budget.Type] = budget;
                index._paths[budget.Type] = relativePath;
            }
            catch (FormatException e)
            {
                found.Add($"Baseline '{relativePath}' is malformed: {e.Message}");
            }
        }

        problems = found.ToImmutable();
        return index;
    }

    /// <summary>
    /// Every type that has a baseline. In compiler-hosted mode this is also the set of types the
    /// analyzer will look for [RestrictedDependency] on.
    /// </summary>
    public ImmutableArray<string> Types => [.. _budgets.Keys];

    /// <summary>
    /// Baselines in type order, so diagnostics come out the same way on every run.
    /// </summary>
    public IEnumerable<BudgetModel> InTypeOrder => _budgets.Values.OrderBy(b => b.Type, StringComparer.Ordinal);

    public bool TryGet(string type, [NotNullWhen(true)] out BudgetModel? budget) => _budgets.TryGetValue(type, out budget);

    /// <summary>
    /// The repo-relative path the baseline for <paramref name="type"/> was read from. False when no
    /// baseline was loaded for it, which is how an orphan is told apart from a type the host named.
    /// </summary>
    public bool TryGetPath(string type, [NotNullWhen(true)] out string? path) => _paths.TryGetValue(type, out path);

    /// <summary>
    /// The budget for one usage key in one project. Rows must match the key exactly, so a parsed
    /// baseline contributes at most one of them: <see cref="BudgetModel.Parse"/> rejects
    /// duplicate rows precisely because summing them would raise a ceiling that no single row's
    /// staleness check could see.
    /// </summary>
    public int BudgetFor(DependencyUsageKey key, string project)
    {
        if (!_budgets.TryGetValue(key.Type, out var budget))
        {
            return 0;
        }

        return budget.Usages
            .Where(s => s.Kind == key.Kind && s.Project == project && s.Site == key.Site && string.Equals(s.Member, key.MemberId, StringComparison.Ordinal))
            .Sum(s => s.Count);
    }
}
