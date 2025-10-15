using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Linq.Expressions;
using System.Linq;
using Microsoft.CodeAnalysis.Text;

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
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocationSyntax = (InvocationExpressionSyntax)context.Node;
        if (invocationSyntax.Expression is not MemberAccessExpressionSyntax ma)
        {
            return;
        }

        var methodName = ma.Name.Identifier.Text;

        // TODO: Other entrypoints
        if (methodName == "IsEnabled")
        {
            AnalyzeIsEnabledInvocation(context, invocationSyntax, ma);
            return;
        }
        else if (methodName == "RequireFeature")
        {
            AnalyzeRequireFeatureMethodCall(context, invocationSyntax, ma);
            return;
        }
    }

    private static void AnalyzeIsEnabledInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocationExpression, MemberAccessExpressionSyntax memberAccessExpression)
    {
        // The feature flag name plus optional default
        if (invocationExpression.ArgumentList.Arguments.Count is not 1 or 2)
        {
            return;
        }

        // TODO: Can we validate that this isnt a static method here?

        var featureFlagService = context.Compilation.GetTypeByMetadataName("Bitwarden.Server.Sdk.Features.IFeatureService");

        if (context.SemanticModel.GetOperation(context.Node, context.CancellationToken) is not IInvocationOperation invocationOperation)
        {
            return;
        }

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

        ReportFeatureFlagRemoval(context, invocationExpression.GetLocation(), flagKey, "isEnabledCheck");
    }

    private static void AnalyzeRequireFeatureMethodCall(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocationExpression, MemberAccessExpressionSyntax memberAccessExpression)
    {
        if (invocationExpression.ArgumentList.Arguments.Count != 1)
        {
            return;
        }

        if (context.SemanticModel.GetOperation(invocationExpression, context.CancellationToken) is not IInvocationOperation invocationOperation)
        {
            return;
        }

        // Once we've moved from syntax -> operations we expect the number of arguments to become 2 since this
        // is an extension method.
        if (invocationOperation.Arguments.Length != 2)
        {
            return;
        }

        var endpointConventionsBuilderType = context.Compilation.GetTypesByMetadataName("Microsoft.AspNetCore.Builder.FeatureEndpointConventionBuilderExtensions")
            .FirstOrDefault(nt => nt.ContainingAssembly.Name == "Bitwarden.Server.Sdk.Features");

        if (!SymbolEqualityComparer.Default.Equals(endpointConventionsBuilderType, invocationOperation.TargetMethod.ContainingType))
        {
            return;
        }

        var stringType = context.Compilation.GetSpecialType(SpecialType.System_String);

        var firstArg = invocationOperation.Arguments[1].Value;

        // There are two versions of the RequireFeature method, one that takes a string and one that takes a function
        // we want to only put the diagnostic on the string variant
        if (!SymbolEqualityComparer.Default.Equals(firstArg.Type, stringType))
        {
            return;
        }

        if (!TryAnalyzeFlagKeyArgument(context, firstArg, out var flagKey))
        {
            return;
        }

        var span = TextSpan.FromBounds(
            memberAccessExpression.OperatorToken.SpanStart,
            invocationExpression.ArgumentList.Span.End
        );

        var location = Location.Create(context.Node.SyntaxTree, span);

        ReportFeatureFlagRemoval(context, location, flagKey, "requireFeatureMethod");
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        if (context.SemanticModel.GetOperation(context.Node, context.CancellationToken) is not IAttributeOperation attributeOperation)
        {
            return;
        }

        var requireFeatureAttributeType = context.Compilation.GetTypeByMetadataName("Bitwarden.Server.Sdk.Features.RequireFeatureAttribute");

        if (requireFeatureAttributeType == null)
        {
            return;
        }

        if (attributeOperation.Operation is not IObjectCreationOperation attributeCreation)
        {
            return;
        }

        if (!SymbolEqualityComparer.Default.Equals(requireFeatureAttributeType, attributeCreation.Type))
        {
            // Different attribute
            return;
        }

        if (attributeCreation.Arguments.Length != 1)
        {
            return;
        }

        if (!TryAnalyzeFlagKeyArgument(context, attributeCreation.Arguments[0].Value, out var flagKey))
        {
            return;
        }

        ReportFeatureFlagRemoval(context, context.Node.GetLocation(), flagKey, "requireFeatureAttribute");
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

    private static void ReportFeatureFlagRemoval(SyntaxNodeAnalysisContext context, Location location, string flagKey, string removalHint)
    {
        var properties = ImmutableDictionary.CreateBuilder<string, string?>();
        properties.Add("flagKey", flagKey);
        properties.Add("removalHint", removalHint);
        context.ReportDiagnostic(Diagnostic.Create(
            _removeFeatureFlagRule,
            location,
            properties.ToImmutableDictionary()
        ));
    }
}
