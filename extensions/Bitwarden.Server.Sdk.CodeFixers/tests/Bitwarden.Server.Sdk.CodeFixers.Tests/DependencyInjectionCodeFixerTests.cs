using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.CodeFixers.Tests;

public class DependencyInjectionCodeFixerTests : CSharpCodeFixTest<DependencyInjectionAnalyzer, DependencyInjectionCodeFixer, DefaultVerifier>
{
    [Fact]
    public async Task Test()
    {
        await RunCodeFixAsync("""

            """,
            """

            """
        );
    }

    private async Task RunCodeFixAsync([StringSyntax("C#-test")] string inputSource, [StringSyntax("C#-test")] string expectedFixedSource)
    {
        TestCode = inputSource;
        FixedCode = expectedFixedSource;

        TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));

        await RunAsync(TestContext.Current.CancellationToken);
    }
}
