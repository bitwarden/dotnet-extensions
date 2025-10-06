using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bitwarden.Server.Sdk.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FeatureFlagAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor _flagShouldBeConstRule = new DiagnosticDescriptor(
        "BW0001",
        "Flag value should be a const",
        "Flag value should be a const",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: ""
    );

    private static readonly DiagnosticDescriptor _removeFeatureFlagRule = new DiagnosticDescriptor(
        "BW0002",
        "Remove feature flag",
        "Remove feature flag",
        "Usage",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: ""
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_flagShouldBeConstRule, _removeFeatureFlagRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var invocationSyntax = (InvocationExpressionSyntax)context.Node;
        if (invocationSyntax.Expression is not MemberAccessExpressionSyntax ma)
        {
            return;
        }

        // TODO: Other entrypoints
        if (ma.Name.Identifier.Text != "IsEnabled")
        {
            return;
        }

        // The feature flag name plus optional default
        if (invocationSyntax.ArgumentList.Arguments.Count is not 1 or 2)
        {
            return;
        }

        var flagArg = invocationSyntax.ArgumentList.Arguments[0].Expression;

        if (flagArg is not IdentifierNameSyntax identifier)
        {
            context.ReportDiagnostic(Diagnostic.Create(_flagShouldBeConstRule, flagArg.GetLocation()));
            return;
        }

        var identifierOperation = context.SemanticModel.GetOperation(identifier, context.CancellationToken);
        if (identifierOperation is not IFieldReferenceOperation fieldRef || !fieldRef.Field.HasConstantValue)
        {
            context.ReportDiagnostic(Diagnostic.Create(_flagShouldBeConstRule, flagArg.GetLocation()));
            return;
        }

        var flagName = (string)fieldRef.Field.ConstantValue;

        // Add flag name to diagnostic so we can use it in the code fixer
        var properties = ImmutableDictionary.CreateRange([new KeyValuePair<string, string?>("flagName", flagName)]);

        context.ReportDiagnostic(Diagnostic.Create(_removeFeatureFlagRule, invocationSyntax.GetLocation(), properties));
    }
}
