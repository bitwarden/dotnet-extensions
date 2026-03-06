using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
        description: "",
        helpLinkUri: HelpUrlFormat
    );

    private static readonly DiagnosticDescriptor _flagKeyShouldBeNonNullOrEmpty = new(
        id: "BW0002",
        title: "Flag key value should be non-null or empty",
        messageFormat: "Flag key value should be non-null or empty",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "",
        helpLinkUri: HelpUrlFormat
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_removeFeatureFlagRule, _flagKeyShouldBeNonNullOrEmpty);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeFlagKeyCollectionAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeFlagKeyCollectionAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetOperation(context.Node, context.CancellationToken) is not IAttributeOperation attributeOperation)
        {
            return;
        }

        var flagKeyCollectionAttributeType = context.Compilation.GetTypeByMetadataName("Bitwarden.Server.Sdk.Features.FlagKeyCollectionAttribute");

        if (flagKeyCollectionAttributeType == null)
        {
            return;
        }

        if (attributeOperation.Operation is not IObjectCreationOperation attributeCreation)
        {
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(flagKeyCollectionAttributeType, attributeCreation.Type))
        {
            // Different attribute
            return;
        }

        if (attributeOperation.Syntax.Parent is not AttributeListSyntax attributeListSyntax)
        {
            return;
        }

        if (attributeListSyntax.Parent is not TypeDeclarationSyntax attachedTypeSyntax)
        {
            return;
        }

        var attachedType = context.SemanticModel.GetDeclaredSymbol(attachedTypeSyntax);

        if (attachedType == null)
        {
            return;
        }

        var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);

        // Analyze all string field constants in the attached type
        var candidateMembers = attachedType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(fs => fs.IsConst && SymbolEqualityComparer.Default.Equals(fs.Type, stringType));

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

            context.ReportDiagnostic(Diagnostic.Create(
                descriptor: _removeFeatureFlagRule,
                location: fieldMember.Locations.First(),
                messageArgs: [constantValue],
                properties: properties.ToImmutableDictionary()
            ));
        }
    }
}
