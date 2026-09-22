using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;

/// <summary>
/// Symbol helpers shared by the attribute readers and the analyzer.
/// </summary>
internal static class SymbolFacts
{
    /// <summary>
    /// The name <see cref="Compilation.GetTypeByMetadataName"/> accepts for <paramref name="type"/>:
    /// dotted namespace, <c>+</c> between nesting levels, arity suffix included.
    /// </summary>
    public static string MetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
        {
            return MetadataName(type.ContainingType) + "+" + type.MetadataName;
        }

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace
            ? type.MetadataName
            : ns.ToDisplayString() + "." + type.MetadataName;
    }

    /// <summary>
    /// The baseline site key for code inside <paramref name="symbol"/>: the documentation-comment
    /// id of the nearest enclosing member that has one. Lambdas and local functions have none, so
    /// their uses key to the method that contains them.
    /// </summary>
    public static string SiteId(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
            {
                continue;
            }

            var id = current.GetDocumentationCommentId();
            if (!string.IsNullOrEmpty(id))
            {
                return id!;
            }
        }

        return symbol.ToDisplayString();
    }

    /// <summary>
    /// The member that directly contains <paramref name="symbol"/>, skipping lambdas and local
    /// functions and mapping an accessor to its property or event; used to find member-level
    /// exception attributes.
    /// </summary>
    public static ISymbol NearestMember(ISymbol symbol)
    {
        var current = symbol;
        while (current is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction } && current.ContainingSymbol is not null)
        {
            current = current.ContainingSymbol;
        }

        return current is IMethodSymbol { AssociatedSymbol: { } owner } ? owner : current;
    }

    /// <summary>
    /// True when <paramref name="symbol"/> is, or is declared inside, <paramref name="type"/>.
    /// </summary>
    public static bool IsWithin(ISymbol symbol, INamedTypeSymbol type)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, type.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The first source location of <paramref name="symbol"/>, or <see cref="Location.None"/>.
    /// </summary>
    public static Location SourceLocation(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(l => l.IsInSource) ?? Location.None;
}
