using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.Analyzers.Tests;

public abstract class AnalyzerTests<TAnalyzer> : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    protected async Task RunAnalyzerAsync([StringSyntax("C#-test")] string source)
    {
        TestCode = source;
        TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        await RunAsync(TestContext.Current.CancellationToken);
    }
}
