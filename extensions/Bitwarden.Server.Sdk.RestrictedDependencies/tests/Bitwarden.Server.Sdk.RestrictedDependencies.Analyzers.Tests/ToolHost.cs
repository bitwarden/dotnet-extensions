using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests;

/// <summary>
/// Drives the analyzer the way a repository's baseline tool does: a seed list of restricted types
/// through analyzer config, BW0017 through the compilation options, and no test harness in
/// between, because that wiring is what the observation tests check.
/// </summary>
internal static class ToolHost
{
    /// <summary>
    /// <paramref name="seedTypes"/> and <paramref name="baselineJson"/> exist because this host is
    /// the only one that can express a seed with the observation channel left off: the analyzer
    /// test harness enables every supported diagnostic, BW0017 included, so a seed there always
    /// lands in real observe mode.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        bool observe,
        bool enableObservations,
        string consumer = SampleProjectFixture.Consumer,
        string? seedTypes = null,
        string? baselineJson = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var references = await ReferenceAssemblies.Net.Net100.ResolveAsync(LanguageNames.CSharp, cancellationToken);

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (enableObservations)
        {
            options = options.WithSpecificDiagnosticOptions(
                ImmutableDictionary<string, ReportDiagnostic>.Empty
                    .Add(DiagnosticDescriptors.Observation.Id, ReportDiagnostic.Info));
        }

        var compilation = CSharpCompilation.Create(
            SampleProjectFixture.Project,
            [
                Parse("/repo/src/Core/RestrictedDependencyAttributes.g.cs", AttributeConstants.Text),
                Parse(SampleProjectFixture.ServicePath, SampleProjectFixture.Service),
                Parse(SampleProjectFixture.ConsumerPath, consumer),
            ],
            references.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location)),
            options);

        // A precondition of this host, not something a test asserts: analyzer results only mean
        // anything if the sample project compiled, so say so here rather than letting a caller's
        // expectations fail for an unrelated reason.
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                $"{nameof(ToolHost)} requires the sample project to compile, but it reported "
                + $"{errors.Count} error(s): {string.Join("; ", errors)}");
        }

        var additionalFiles = baselineJson is null
            ? ImmutableArray<AdditionalText>.Empty
            : ImmutableArray.Create<AdditionalText>(new BaselineFile($"/repo/baselines/{SampleProjectFixture.Type}.json", baselineJson));

        var analyzerOptions = new AnalyzerOptions(additionalFiles, Options(observe, seedTypes, markBaselines: baselineJson is not null));
        var withAnalyzers = new CompilationWithAnalyzers(
            compilation,
            [new RestrictedDependencyAnalyzer()],
            new CompilationWithAnalyzersOptions(analyzerOptions, onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false));

        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
    }

    /// <summary>
    /// The property bags of every BW0017 diagnostic carrying the given row shape.
    /// </summary>
    public static List<ImmutableDictionary<string, string?>> Rows(ImmutableArray<Diagnostic> diagnostics, string row) =>
    [
        .. diagnostics
            .Where(d => d.Id == DiagnosticDescriptors.Observation.Id
                && d.Properties.TryGetValue(ObservationConstants.Row, out var value) && value == row)
            .Select(d => d.Properties),
    ];

    /// <summary>
    /// The one usage row of the given access shape; fails when there is none or more than one.
    /// </summary>
    public static ImmutableDictionary<string, string?> OfKind(
        IEnumerable<ImmutableDictionary<string, string?>> usages,
        string kind) =>
        Assert.Single(usages, u => u[ObservationConstants.UsageKind] == kind);

    private static SyntaxTree Parse(string path, string text) =>
        CSharpSyntaxTree.ParseText(SourceText.From(text), path: path);

    /// <summary>
    /// The global analyzer config a baseline tool generates. Naming a seed type is one of the two
    /// halves of observe mode; the other is BW0017, which only the compilation options carry.
    /// </summary>
    private static TestAnalyzerConfigOptionsProvider Options(bool observe, string? seedTypes, bool markBaselines)
    {
        var global = new Dictionary<string, string>
        {
            [AnalyzerConfigConstants.Analysis] = "true",
            [AnalyzerConfigConstants.RepositoryRoot] = SampleProjectFixture.RepoRoot,
        };

        if (observe)
        {
            global[AnalyzerConfigConstants.SeedTypes] = seedTypes ?? SampleProjectFixture.Type;
        }

        var fileOptions = markBaselines
            ? new Dictionary<string, string> { [AnalyzerConfigConstants.BaselineMetadata] = "true" }
            : null;

        return new TestAnalyzerConfigOptionsProvider(global, fileOptions);
    }

    /// <summary>
    /// A committed baseline as the build hands it to the analyzer: an <c>AdditionalFiles</c> entry
    /// whose content is read through <see cref="AdditionalText"/>.
    /// </summary>
    private sealed class BaselineFile(string path, string json) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(json);
    }
}
