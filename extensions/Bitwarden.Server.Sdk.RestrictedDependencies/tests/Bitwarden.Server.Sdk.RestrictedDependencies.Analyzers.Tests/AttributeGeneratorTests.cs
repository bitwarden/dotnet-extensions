using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests;

/// <summary>
/// The analyzer tests compile <see cref="AttributeConstants.Text"/> in by hand
/// because the harness runs no generators, so this is the only place the generator itself runs:
/// it must emit the attributes into an analyzed project and nothing into any other.
/// </summary>
public class AttributeGeneratorTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    public void EnabledProject_ReceivesTheAttributeSource(string analysis)
    {
        var result = Run(analysis);

        var generated = Assert.Single(result.GeneratedSources);
        Assert.Equal(AttributeConstants.HintName, generated.HintName);
        Assert.Equal(AttributeConstants.Text, generated.SourceText.ToString());
    }

    [Fact]
    public void EmittedSource_CompilesWithoutErrors()
    {
        var compilation = Compile();
        var driver = Driver("true").RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _, TestContext.Current.CancellationToken);

        Assert.Single(driver.GetRunResult().GeneratedTrees);
        Assert.Empty(updated.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData(null)]
    public void ProjectOutOfScope_ReceivesNothing(string? analysis)
    {
        var result = Run(analysis);

        Assert.Empty(result.GeneratedSources);
    }

    private static GeneratorRunResult Run(string? analysis)
    {
        var driver = Driver(analysis).RunGenerators(Compile(), TestContext.Current.CancellationToken);
        return Assert.Single(driver.GetRunResult().Results);
    }

    private static GeneratorDriver Driver(string? analysis)
    {
        var options = analysis is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [AnalyzerConfigConstants.Analysis] = analysis };

        return CSharpGeneratorDriver.Create(
            generators: [new AttributeGenerator().AsSourceGenerator()],
            optionsProvider: new TestAnalyzerConfigOptionsProvider(options));
    }

    private static CSharpCompilation Compile()
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        return CSharpCompilation.Create(
            "GeneratorTest",
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Join(runtimeDirectory, "System.Runtime.dll")),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

}
