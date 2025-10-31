using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Bitwarden.Server.Sdk.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class DependencyInjectionAnalyzer : DiagnosticAnalyzer
{
    private static Regex _addMethodRegex = new("^Add?[Keyed][Singleton|Scoped|Transient]", RegexOptions.Compiled);

    private static readonly DiagnosticDescriptor _shouldUseTryAddOverload = new(
        "BW0003",
        "Should use TryAdd overloads",
        "Should use TryAdd overloads",
        "Usage",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: ""
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_shouldUseTryAddOverload);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocationSyntax = (InvocationExpressionSyntax)context.Node;

        if (invocationSyntax.Expression is not MemberAccessExpressionSyntax ma)
        {
            return;
        }

        var methodName = ma.Name.Identifier.Text;

        var match = _addMethodRegex.Match(methodName);

        if (!match.Success
            || context.SemanticModel.GetOperation(invocationSyntax) is not IInvocationOperation invocationOperation
            || invocationOperation.TargetMethod.ReceiverType is null)
        {
            return;
        }

        var targetType = context.Compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions");

        if (!SymbolEqualityComparer.Default.Equals(invocationOperation.TargetMethod.ReceiverType, targetType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            _shouldUseTryAddOverload,
            invocationSyntax.GetLocation()
        ));
    }
}
