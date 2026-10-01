using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// BW0014, plus the BW0015 that fires when a restricted type has no baseline at all. Compares the
/// members a sealed type declares against the set its baseline recorded, and captures that set so
/// a baseline tool can write it back.
/// </summary>
internal sealed class DeclaredMemberSetChecker
{
    private readonly BudgetIndex _budgets;
    private readonly RestrictedTypeIndex _restrictedTypes;
    private readonly bool _enforce;
    private readonly string _updateInstruction;
    private readonly List<DeclaredMemberSet> _observed = [];

    /// <summary>
    /// When <paramref name="enforce"/> is false the checker only captures member sets, which is what a
    /// baseline tool wants: it is rebuilding the baseline, so the baseline cannot be the authority.
    /// </summary>
    public DeclaredMemberSetChecker(BudgetIndex budgets, RestrictedTypeIndex restrictedTypes, bool enforce, string updateInstruction)
    {
        _budgets = budgets;
        _restrictedTypes = restrictedTypes;
        _enforce = enforce;
        _updateInstruction = updateInstruction;
    }

    /// <summary>
    /// The declared member set of each sealed restricted type seen, in type order.
    /// </summary>
    public ImmutableArray<DeclaredMemberSet> Observed
    {
        get
        {
            lock (_observed)
            {
                return [.. _observed.OrderBy(d => d.Type, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>
    /// Checks one type that carries <c>[RestrictedDependency]</c>, recording its member set and
    /// returning any diagnostic it earns. Eager on purpose: recording the member set is a side effect
    /// the baseline depends on, and an iterator would skip it whenever a caller ignored the result.
    /// </summary>
    public ImmutableArray<Diagnostic> Check(INamedTypeSymbol type)
    {
        var found = ImmutableArray.CreateBuilder<Diagnostic>();
        var metadataName = SymbolFacts.MetadataName(type);
        var hasBaseline = _budgets.TryGet(metadataName, out var budget);
        if (!hasBaseline && _enforce)
        {
            found.Add(Diagnostic.Create(
                DiagnosticDescriptors.AttributeInvalid,
                SymbolFacts.SourceLocation(type),
                $"'{type.Name}' carries [RestrictedDependency] but no baseline named '{BudgetModel.FileNameFor(metadataName)}' was supplied; {_updateInstruction}."));
        }

        if (!_restrictedTypes.TryGet(type, out var model))
        {
            // The type is attributed but outside this compilation's restricted-type set. Reading it directly
            // still lets the build check its member set; a baseline tool has nothing to check it against.
            model = RestrictedTypeModel.TryRead(type);
            if (model is null || !_enforce)
            {
                return found.ToImmutable();
            }
        }

        if (!model.IsSealed)
        {
            return found.ToImmutable();
        }

        var declared = DeclaredMembers(type, model);
        lock (_observed)
        {
            _observed.Add(new DeclaredMemberSet(metadataName, [.. declared.Keys]));
        }

        if (!hasBaseline || !_enforce)
        {
            return found.ToImmutable();
        }

        var allowed = new HashSet<string>(budget!.DeclaredMembers, StringComparer.Ordinal);
        foreach (var pair in declared)
        {
            if (!allowed.Contains(pair.Key))
            {
                found.Add(Diagnostic.Create(
                    DiagnosticDescriptors.SealedTypeGrew, SymbolFacts.SourceLocation(pair.Value), type.Name, model.Tracking, pair.Value.Name));
            }
        }

        return found.ToImmutable();
    }

    /// <summary>
    /// The members the seal covers, keyed by documentation-comment id so the set is comparable
    /// across renames of anything but the member itself.
    /// </summary>
    private static SortedDictionary<string, ISymbol> DeclaredMembers(INamedTypeSymbol type, RestrictedTypeModel model)
    {
        var declared = new SortedDictionary<string, ISymbol>(StringComparer.Ordinal);
        Collect(type, nested: false);
        return declared;

        void Collect(INamedTypeSymbol current, bool nested)
        {
            foreach (var member in current.GetMembers())
            {
                if (member.IsImplicitlyDeclared)
                {
                    continue;
                }

                var include = member switch
                {
                    IMethodSymbol { MethodKind: MethodKind.Ordinary } => !nested && model.SealMembers,
                    IEventSymbol => !nested && model.SealMembers,
                    IPropertySymbol => (!nested && (model.SealMembers || model.SealProperties)) || (nested && model.SealProperties),
                    INamedTypeSymbol => !nested && model.SealNestedTypes,
                    _ => false,
                };

                if (!include)
                {
                    continue;
                }

                var id = member.GetDocumentationCommentId();
                if (id is not null)
                {
                    declared[id] = member;
                }

                if (member is INamedTypeSymbol nestedType && model.SealNestedTypes && model.SealProperties)
                {
                    Collect(nestedType, nested: true);
                }
            }
        }
    }
}
