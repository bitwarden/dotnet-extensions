using System.Diagnostics.CodeAnalysis;
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

        // TODO: Can we validate that this isnt a static method here?

        var featureFlagService = context.Compilation.GetTypeByMetadataName("Bitwarden.Server.Sdk.Features.IFeatureService");

        var invocationOperation = (IInvocationOperation)context.SemanticModel.GetOperation(context.Node, context.CancellationToken);

        if (invocationOperation.Instance is null)
        {
            // Static method, not us
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(featureFlagService, invocationOperation.Instance.Type))
        {
            // Method doesn't belong to us
            return;
        }

        // We previously validated there is 1 or 2 arguments so this array access should be safe
        if (!TryAnalyzeFlagKeyArgument(context, invocationOperation.Arguments[0].Value, out var flagKey))
        {
            return;
        }

        // Add flag name to diagnostic so we can use it in the code fixer
        var properties = ImmutableDictionary.CreateRange([new KeyValuePair<string, string?>("flagName", flagKey)]);

        context.ReportDiagnostic(Diagnostic.Create(_removeFeatureFlagRule, invocationSyntax.GetLocation(), properties));
    }

    private static bool TryAnalyzeFlagKeyArgument(SyntaxNodeAnalysisContext context, IOperation flagKeyOperation, [MaybeNullWhen(false)] out string flagKey)
    {
        if (flagKeyOperation is not IFieldReferenceOperation fieldRef
            || !fieldRef.Field.HasConstantValue)
        {
            context.ReportDiagnostic(Diagnostic.Create(_flagShouldBeConstRule, flagKeyOperation.Syntax.GetLocation()));
            flagKey = null;
            return false;
        }

        // TODO: Warn on null flag key
        flagKey = (string)fieldRef.Field.ConstantValue!;
        return true;
    }
}
