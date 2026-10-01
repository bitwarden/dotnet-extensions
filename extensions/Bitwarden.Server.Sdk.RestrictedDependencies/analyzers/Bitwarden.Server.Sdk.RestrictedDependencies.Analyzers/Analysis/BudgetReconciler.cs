using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis.Usage;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// Reconciles what the callbacks observed against what the baseline recorded, once the counts are
/// final. Uses beyond the budget are BW0005-BW0009 on the surplus locations; budget the code no
/// longer uses is BW0013. Both directions run only for gated rules: a tracked-only entry is a
/// snapshot for reporting, not a promise.
/// </summary>
internal sealed class BudgetReconciler
{
    private readonly Compilation _compilation;
    private readonly BudgetIndex _budgets;
    private readonly RestrictedTypeIndex _restrictedTypes;
    private readonly DependencyUsageLedger _ledger;
    private readonly string _project;
    private readonly string _updateInstruction;

    public BudgetReconciler(
        Compilation compilation,
        BudgetIndex budgets,
        RestrictedTypeIndex restrictedTypes,
        DependencyUsageLedger ledger,
        string project,
        string updateInstruction)
    {
        _compilation = compilation;
        _budgets = budgets;
        _restrictedTypes = restrictedTypes;
        _ledger = ledger;
        _project = project;
        _updateInstruction = updateInstruction;
    }

    /// <summary>
    /// One diagnostic per use past the budget. The surplus is taken from the end of the ordered
    /// locations so that the sites which fit the baseline are the stable ones and a newly added use
    /// is what gets reported.
    /// </summary>
    public ImmutableArray<Diagnostic> ExcessUses()
    {
        var found = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var (key, record) in _ledger.InKeyOrder)
        {
            var useRule = record.Model.RuleFor(record.Member);
            if (!useRule.IsGated)
            {
                continue;
            }

            var excess = record.Count - _budgets.BudgetFor(key, _project);
            if (excess <= 0)
            {
                continue;
            }

            var ordered = record.Locations
                .OrderBy(l => l.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(l => l.SourceSpan.Start)
                .ToList();
            foreach (var location in ordered.Skip(ordered.Count - excess))
            {
                found.Add(DependencyUsageDiagnosticFactory.Create(key.Kind, record.Model, useRule, record.Member, record.Subject, location));
            }
        }

        return found.ToImmutable();
    }

    /// <summary>
    /// One BW0013 per baseline row this project no longer satisfies, so the budget can only be
    /// spent down deliberately rather than drift.
    /// </summary>
    public ImmutableArray<Diagnostic> StaleEntries()
    {
        var found = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var baseline in _budgets.InTypeOrder)
        {
            var type = _compilation.GetTypeByMetadataName(baseline.Type);
            RestrictedTypeModel? model = null;
            if (type is not null)
            {
                _restrictedTypes.TryGet(type, out model);
            }

            foreach (var entry in baseline.Usages.Where(s => s.Project == _project))
            {
                if (model is not null && !IsGated(model, entry.Member))
                {
                    continue;
                }

                var key = new DependencyUsageKey(baseline.Type, entry.Kind, entry.Member, entry.Site);
                var observed = _ledger.CountFor(key);
                if (observed >= entry.Count)
                {
                    continue;
                }

                var siteSymbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(entry.Site, _compilation);
                found.Add(Diagnostic.Create(
                    DiagnosticDescriptors.StaleBaseline,
                    siteSymbol is null ? Location.None : SymbolFacts.SourceLocation(siteSymbol),
                    baseline.Type,
                    entry.Count,
                    entry.Member is null ? entry.Kind.ToToken() + (entry.Count == 1 ? " use" : " uses") : $"use(s) of '{entry.Member}'",
                    entry.Site,
                    _project,
                    observed,
                    _updateInstruction));
            }
        }

        return found.ToImmutable();
    }

    /// <summary>
    /// Whether the row's rule is gated. A member row resolves the member first; a row whose
    /// member no longer resolves is treated as gated so its disappearance is still reported.
    /// </summary>
    private bool IsGated(RestrictedTypeModel model, string? memberId)
    {
        if (memberId is null)
        {
            return model.Default.IsGated;
        }

        var member = DocumentationCommentId.GetFirstSymbolForDeclarationId(memberId, _compilation);
        return member is null || model.RuleFor(member).IsGated;
    }
}
