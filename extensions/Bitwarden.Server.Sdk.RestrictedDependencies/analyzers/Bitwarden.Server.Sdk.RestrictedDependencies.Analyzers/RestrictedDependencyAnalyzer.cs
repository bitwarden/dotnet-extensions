using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers;

/// <summary>
/// Enforces the usage budget of every <c>[RestrictedDependency]</c> type visible to a compilation
/// that has RestrictedDependencyAnalysis enabled. A repository's baseline tool hosts this same
/// analyzer out of band, configured through the same analyzer options the compiler passes, so the
/// baseline is by construction what the build would have checked.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RestrictedDependencyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticDescriptors.All;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // Generated code is analyzed and reported like any other: an auto-generated header,
        // [GeneratedCode] or generated_code = true would otherwise be a one-line bypass of the gate.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(start =>
        {
            if (!RestrictedDependencyConfig.IsEnabled(start.Options.AnalyzerConfigOptionsProvider.GlobalOptions))
            {
                return;
            }

            var coordinator = new CompilationCoordinator(start.Compilation, start.Options, start.CancellationToken);
            start.RegisterSymbolAction(coordinator.OnNamedType, SymbolKind.NamedType);
            start.RegisterSymbolAction(coordinator.OnMethod, SymbolKind.Method);
            start.RegisterSymbolAction(coordinator.OnProperty, SymbolKind.Property);
            start.RegisterSymbolAction(coordinator.OnField, SymbolKind.Field);
            start.RegisterSymbolAction(coordinator.OnEvent, SymbolKind.Event);
            // A local function sits behind OnOperation's restricted-type short-circuit, and it is
            // the one declared thing no symbol callback reaches, so it gets its own registration.
            start.RegisterOperationAction(coordinator.OnLocalFunction, OperationKind.LocalFunction);
            start.RegisterOperationAction(
                coordinator.OnOperation,
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.MethodReference,
                OperationKind.EventReference,
                OperationKind.FieldReference,
                OperationKind.ObjectCreation,
                OperationKind.TypeOf);
            start.RegisterSyntaxTreeAction(coordinator.OnSyntaxTree);
            start.RegisterCompilationEndAction(coordinator.OnCompilationEnd);
        });
    }
}
