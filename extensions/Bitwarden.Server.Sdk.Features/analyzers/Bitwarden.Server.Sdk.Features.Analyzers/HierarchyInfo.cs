using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.Features.Analyzers;

internal sealed record TypeInfo(string QualifiedName, bool IsRecord);

internal sealed record HierarchyInfo(string FilenameHint, string MetadataName, string Namespace, EquatableArray<TypeInfo> Hierarchy)
{
    public static HierarchyInfo From(INamedTypeSymbol typeSymbol)
    {
        var hierarchy = new List<TypeInfo>();

        for (var parent = typeSymbol; parent is not null; parent = parent.ContainingType)
        {
            hierarchy.Add(new TypeInfo(
                parent.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                parent.IsRecord
            ));
        }

        return new(
            typeSymbol.GetFullyQualifiedMetadataName(),
            typeSymbol.MetadataName,
            typeSymbol.ContainingNamespace.ToDisplayString(new(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)),
            new EquatableArray<TypeInfo>([.. hierarchy])
        );
    }
}
