using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Rules;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// Resolves a restricted type to its model, and answers the three derived questions the usage
/// kinds ask: what a type implements, which restricted member a call lands on, and what is buried
/// in a generic argument. The answers are cached because the callbacks ask them repeatedly for the
/// same symbols.
/// </summary>
internal sealed class RestrictedTypeIndex
{
    private readonly ImmutableDictionary<INamedTypeSymbol, RestrictedTypeModel> _models;

    private readonly ConcurrentDictionary<INamedTypeSymbol, ImmutableArray<RestrictedTypeModel>> _implementationCache =
        new(SymbolEqualityComparer.Default);

    private readonly ConcurrentDictionary<INamedTypeSymbol, ImmutableDictionary<ISymbol, (RestrictedTypeModel Model, ISymbol Member)>> _memberMapCache =
        new(SymbolEqualityComparer.Default);

    private RestrictedTypeIndex(
        ImmutableDictionary<INamedTypeSymbol, RestrictedTypeModel> models,
        ImmutableArray<(string Message, Location Location)> problems,
        ImmutableArray<string> namesWithoutAttribute)
    {
        _models = models;
        Problems = problems;
        NamesWithoutAttribute = namesWithoutAttribute;
    }

    /// <summary>
    /// Reads the model off each named type that this compilation can see. A name the compilation
    /// does not reference is skipped silently; a name it does reference but that carries no
    /// attribute is reported through <see cref="NamesWithoutAttribute"/>.
    /// </summary>
    public static RestrictedTypeIndex Resolve(Compilation compilation, ImmutableArray<string> restrictedTypeNames)
    {
        var models = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, RestrictedTypeModel>(SymbolEqualityComparer.Default);
        var problems = ImmutableArray.CreateBuilder<(string, Location)>();
        var unattributed = ImmutableArray.CreateBuilder<string>();

        foreach (var name in restrictedTypeNames)
        {
            var type = compilation.GetTypeByMetadataName(name);
            if (type is null)
            {
                continue;
            }

            var model = RestrictedTypeModel.TryRead(type);
            if (model is null)
            {
                unattributed.Add(name);
                continue;
            }

            problems.AddRange(model.Problems);
            models[type] = model;
        }

        return new RestrictedTypeIndex(models.ToImmutable(), problems.ToImmutable(), unattributed.ToImmutable());
    }

    /// <summary>
    /// True when this compilation sees no restricted type, in which case every usage kind can
    /// short-circuit without touching a symbol.
    /// </summary>
    public bool IsEmpty => _models.IsEmpty;

    /// <summary>
    /// Attribute problems to report as BW0015, each with the location of the offending attribute.
    /// </summary>
    public ImmutableArray<(string Message, Location Location)> Problems { get; }

    /// <summary>
    /// Names that resolved to a type carrying no <c>[RestrictedDependency]</c>. With a baseline
    /// behind them these are orphans; without one the host simply named a type this compilation
    /// does not restrict.
    /// </summary>
    public ImmutableArray<string> NamesWithoutAttribute { get; }

    public bool TryGet(INamedTypeSymbol type, [NotNullWhen(true)] out RestrictedTypeModel? model) => _models.TryGetValue(type, out model);

    /// <summary>
    /// Owner and tracking for every type resolved here, in type order, as BW0017 rows.
    /// </summary>
    public IEnumerable<Diagnostic> Observations() =>
        _models.Values
            .Select(p => (Type: p.MetadataName, p.Owner, p.Tracking))
            .OrderBy(p => p.Type, StringComparer.Ordinal)
            .Select(p => ObservationDiagnosticFactory.RestrictedType(p.Type, p.Owner, p.Tracking));

    /// <summary>
    /// The restricted types <paramref name="type"/> implements, derives from, or extends. An
    /// interface that extends a restricted interface counts: it carries every restricted member
    /// past a classifier that only looked for the restricted type itself, so typed code could
    /// route around the ratchet entirely. The restricted type itself is never its own
    /// implementation.
    /// </summary>
    public ImmutableArray<RestrictedTypeModel> ImplementedBy(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface) || _models.ContainsKey(type.OriginalDefinition))
        {
            return [];
        }

        return _implementationCache.GetOrAdd(type.OriginalDefinition, t =>
        {
            var builder = ImmutableArray.CreateBuilder<RestrictedTypeModel>();
            foreach (var model in _models.Values)
            {
                var restricted = model.Type.OriginalDefinition;
                var matches = restricted.TypeKind == TypeKind.Interface
                    ? t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, restricted))
                    : BaseTypes(t).Any(b => SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, restricted));
                if (matches)
                {
                    builder.Add(model);
                }
            }

            return builder.ToImmutable();
        });

        static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                yield return current;
            }
        }
    }

    /// <summary>
    /// The restricted member a use of <paramref name="member"/> lands on: the member itself when
    /// its owner is restricted, otherwise the interface member it implements.
    /// </summary>
    public bool TryMapMember(ISymbol member, out (RestrictedTypeModel Model, ISymbol Member) mapped)
    {
        mapped = default;
        var owner = member.ContainingType;
        if (owner is null)
        {
            return false;
        }

        if (_models.TryGetValue(owner.OriginalDefinition, out var direct))
        {
            mapped = (direct, member.OriginalDefinition);
            return true;
        }

        var map = _memberMapCache.GetOrAdd(owner.OriginalDefinition, BuildMemberMap);
        return map.TryGetValue(member.OriginalDefinition, out mapped);
    }

    /// <summary>
    /// Restricted types appearing anywhere inside <paramref name="type"/>: generic arguments,
    /// array elements and tuple elements. The type itself is not included.
    /// </summary>
    public IEnumerable<RestrictedTypeModel> NestedIn(ITypeSymbol type)
    {
        var found = new HashSet<RestrictedTypeModel>();
        // Following type-parameter constraints makes the walk cyclic: `where T : IFoo<T>` would
        // otherwise recurse forever.
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        Visit(type, top: true);
        return found;

        void Visit(ITypeSymbol current, bool top)
        {
            if (!visited.Add(current))
            {
                return;
            }

            switch (current)
            {
                case INamedTypeSymbol named:
                    if (!top && _models.TryGetValue(named.OriginalDefinition, out var model))
                    {
                        found.Add(model);
                    }

                    foreach (var argument in named.TypeArguments)
                    {
                        Visit(argument, top: false);
                    }

                    break;

                case IArrayTypeSymbol array:
                    Visit(array.ElementType, top: false);
                    break;

                case ITypeParameterSymbol typeParameter:
                    // `Lazy<T> where T : IUserService` buries the restricted type just as surely
                    // as `Lazy<IUserService>` does.
                    foreach (var constraint in typeParameter.ConstraintTypes)
                    {
                        Visit(constraint, top: false);
                    }

                    break;
            }
        }
    }

    private ImmutableDictionary<ISymbol, (RestrictedTypeModel Model, ISymbol Member)> BuildMemberMap(INamedTypeSymbol implementation)
    {
        var builder = ImmutableDictionary.CreateBuilder<ISymbol, (RestrictedTypeModel, ISymbol)>(SymbolEqualityComparer.Default);
        foreach (var model in ImplementedBy(implementation))
        {
            if (model.Type.TypeKind != TypeKind.Interface)
            {
                continue;
            }

            foreach (var interfaceMember in model.Type.GetMembers())
            {
                var implemented = implementation.FindImplementationForInterfaceMember(interfaceMember);
                if (implemented is not null && !builder.ContainsKey(implemented.OriginalDefinition))
                {
                    builder[implemented.OriginalDefinition] = (model, interfaceMember);
                }
            }
        }

        return builder.ToImmutable();
    }
}
