using System.Text;
using Microsoft.CodeAnalysis;

namespace Bitwarden.Server.Sdk.Features;

internal static class TypeSymbolExtensions
{
    public static string GetFullyQualifiedMetadataName(this ITypeSymbol symbol)
    {
        var sb = new StringBuilder();
        symbol.AppendFullyQualifiedMetadataName(sb);
        return sb.ToString();
    }

    private static void AppendFullyQualifiedMetadataName(this ISymbol symbol, StringBuilder sb)
    {
        static void BuildFrom(ISymbol? symbol, StringBuilder builder)
        {
            switch (symbol)
            {
                case INamespaceSymbol { ContainingNamespace.IsGlobalNamespace: false }:
                    BuildFrom(symbol.ContainingNamespace, builder);
                    builder.Append('.');
                    builder.Append(symbol.MetadataName);
                    break;
                case INamespaceSymbol { IsGlobalNamespace: false }:
                    builder.Append(symbol.MetadataName);
                    break;
                case ITypeSymbol { ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true } }:
                    builder.Append(symbol.MetadataName);
                    break;
                case ITypeSymbol { ContainingSymbol: INamespaceSymbol namespaceSymbol }:
                    BuildFrom(namespaceSymbol, builder);
                    builder.Append('.');
                    builder.Append(symbol.MetadataName);
                    break;
                case ITypeSymbol { ContainingSymbol: ITypeSymbol typeSymbol }:
                    BuildFrom(typeSymbol, builder);
                    builder.Append('+');
                    builder.Append(symbol.MetadataName);
                    break;
                default:
                    break;
            }
        }

        BuildFrom(symbol, sb);
    }
}
