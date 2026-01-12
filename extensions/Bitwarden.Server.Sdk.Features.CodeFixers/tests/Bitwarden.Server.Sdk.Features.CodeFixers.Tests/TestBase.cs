using System.Collections.Immutable;
using Bitwarden.Server.Sdk.Features.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.CodeFixers.Tests;

public class TestBase : CodeFixTest<DefaultVerifier>
{
    public override string Language => LanguageNames.CSharp;

    public override Type SyntaxKindType => typeof(SyntaxKind);

    protected override string DefaultFileExt => "cs";

    protected override IEnumerable<Type> GetSourceGenerators()
    {
        return [typeof(FeaturesGenerator)];
    }

    protected override CompilationOptions CreateCompilationOptions()
        => new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);
    protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.Default, DocumentationMode.Diagnose);

    protected override IEnumerable<CodeFixProvider> GetCodeFixProviders()
    {
        return [new RemoveFeatureFlagCodeFixer()];
    }

    protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers()
    {
        return [];
    }

    protected override CompilationWithAnalyzers CreateCompilationWithAnalyzers(Compilation compilation, ImmutableArray<DiagnosticAnalyzer> analyzers, AnalyzerOptions options, CancellationToken cancellationToken) => base.CreateCompilationWithAnalyzers(compilation, analyzers, options, cancellationToken);
}
