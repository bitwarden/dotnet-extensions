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
    /// id of <paramref name="symbol"/> when it is a type, otherwise of the nearest type that
    /// contains it. Changing a member's parameters or name, or moving a use between members of the
    /// same type, leaves the key unchanged. Uses inside lambdas and local functions key to the
    /// type that contains them.
    /// </summary>
    public static string SiteId(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamedTypeSymbol type && type.GetDocumentationCommentId() is { Length: > 0 } id)
            {
                return id;
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
