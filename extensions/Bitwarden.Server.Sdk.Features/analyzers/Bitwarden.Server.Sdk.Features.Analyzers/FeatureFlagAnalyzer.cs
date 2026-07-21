using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitwarden.Server.Sdk.Features.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FeatureFlagAnalyzer : DiagnosticAnalyzer
{
    const string HelpUrlFormat = "https://github.com/bitwarden/dotnet-extensions/blob/main/docs/diagnostics.md#{0}";

    internal static readonly DiagnosticDescriptor _removeFeatureFlagRule = new(
        id: "BW0001",
        title: "Feature flags should be removed once not used",
        messageFormat: "Remove feature flag",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: string.Format(HelpUrlFormat, "BW0001")
    );

    private static readonly DiagnosticDescriptor _flagKeyShouldBeNonNullOrEmpty = new(
        id: "BW0002",
        title: "Flag key value should not be null or empty",
        messageFormat: "Flag key value should not be null or empty",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: string.Format(HelpUrlFormat, "BW0002")
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(_removeFeatureFlagRule, _flagKeyShouldBeNonNullOrEmpty);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        // Use RegisterSymbolAction rather than RegisterSyntaxNodeAction so the framework
        // provides pre-resolved INamedTypeSymbol objects. SemanticModel access inside
        // SyntaxNodeAnalysisContext fails silently in roslyn-language-server's LSP host
        // (GetSymbolInfo, GetTypeInfo, GetDeclaredSymbol, and GetAttributes all return
        // null/empty for attribute types from referenced NuGet packages in that context).
        context.RegisterSymbolAction(AnalyzeFlagKeyCollectionSymbol, SymbolKind.NamedType);
    }

    private static void AnalyzeFlagKeyCollectionSymbol(SymbolAnalysisContext context)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;

        // Check if this type has [FlagKeyCollection] by name and namespace.
        // AttributeData.AttributeClass is the resolved attribute type symbol; comparing
        // MetadataName + namespace avoids GetTypeByMetadataName (which can fail when
        // the assembly can't be resolved in the LSP incremental-compilation context).
        var hasFlagKeyCollection = typeSymbol.GetAttributes().Any(a =>
            a.AttributeClass?.MetadataName == "FlagKeyCollectionAttribute" &&
            a.AttributeClass.ContainingNamespace?.ToDisplayString() == "Bitwarden.Server.Sdk.Features");

        if (!hasFlagKeyCollection)
        {
            return;
        }

        var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);

        // Analyze all string field constants in the type.
        var candidateMembers = typeSymbol.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(fs => fs.IsConst && SymbolEqualityComparer.Default.Equals(fs.Type, stringType))
            .ToList();

        foreach (var fieldMember in candidateMembers)
        {
            var constantValue = (string?)fieldMember.ConstantValue;
            if (string.IsNullOrWhiteSpace(constantValue))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    _flagKeyShouldBeNonNullOrEmpty,
                    fieldMember.Locations.First()
                ));
                continue;
            }

            var properties = ImmutableDictionary.CreateBuilder<string, string?>();
            properties.Add("FlagKey", constantValue);

            var diag = Diagnostic.Create(
                descriptor: _removeFeatureFlagRule,
                location: fieldMember.Locations.First(),
                messageArgs: [constantValue],
                properties: properties.ToImmutableDictionary()
            );
            context.ReportDiagnostic(diag);
        }
    }
}
