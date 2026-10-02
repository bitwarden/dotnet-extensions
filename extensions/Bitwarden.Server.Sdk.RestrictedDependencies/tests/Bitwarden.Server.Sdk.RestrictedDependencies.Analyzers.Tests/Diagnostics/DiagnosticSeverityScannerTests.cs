using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Diagnostics;

/// <summary>
/// BW0016 under the analyzer. <see cref="DiagnosticSeverityScanner.Run"/> takes a bare
/// <see cref="Compilation"/>, so every configuration source it reads is reachable without the
/// analyzer test harness. The two harness tests at the end prove the analyzer still calls it, and
/// that the expiry warning stays configurable.
/// </summary>
public class DiagnosticSeverityScannerTests
{
    /// <summary>
    /// Where a lowering came from. Compiler options are how <c>NoWarn</c> and
    /// <c>WarningsNotAsErrors</c> arrive, which is the way a consuming repository would most
    /// plausibly switch the ratchet off.
    /// </summary>
    public enum LoweringSource
    {
        /// <summary>
        /// <c>&lt;NoWarn&gt;</c> or <c>&lt;WarningsNotAsErrors&gt;</c>, read from
        /// <see cref="CompilationOptions.SpecificDiagnosticOptions"/>.
        /// </summary>
        CompilerOptions,

        /// <summary>
        /// A <c>.globalconfig</c>, read through <see cref="SyntaxTreeOptionsProvider"/>.
        /// </summary>
        GlobalAnalyzerConfig,

        /// <summary>
        /// An <c>.editorconfig</c> that applies to a directory of the compilation.
        /// </summary>
        EditorConfig,
    }

    private const string RatchetId = "BW0005";
    private const string ApiFile = "/repo/src/Api/Consumer.cs";
    private const string ApiEditorConfig = "/repo/src/Api/.editorconfig";

    [Theory]
    [InlineData(LoweringSource.CompilerOptions, ReportDiagnostic.Suppress, "suppress", "compiler options (NoWarn or WarningsNotAsErrors)")]
    [InlineData(LoweringSource.CompilerOptions, ReportDiagnostic.Warn, "warn", "compiler options (NoWarn or WarningsNotAsErrors)")]
    [InlineData(LoweringSource.GlobalAnalyzerConfig, ReportDiagnostic.Info, "info", "a global analyzer config")]
    [InlineData(LoweringSource.EditorConfig, ReportDiagnostic.Hidden, "hidden", "an .editorconfig applying to 'src/Api/'")]
    public void Run_ReportsWhicheverConfigurationLoweredARatchetId(
        LoweringSource source,
        ReportDiagnostic report,
        string severity,
        string expectedSource)
    {
        var diagnostic = Assert.Single(ScanCompilation(source, report, ApiFile));

        Assert.Equal(DiagnosticDescriptors.SeverityLowered.Id, diagnostic.Id);
        Assert.Equal(
            $"{RatchetId} is configured as '{severity}' by {expectedSource}; restricted dependency diagnostics must remain errors",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(LoweringSource.CompilerOptions)]
    [InlineData(LoweringSource.GlobalAnalyzerConfig)]
    [InlineData(LoweringSource.EditorConfig)]
    public void Run_LeavesARatchetIdConfiguredAsErrorAlone(LoweringSource source)
    {
        Assert.Empty(ScanCompilation(source, ReportDiagnostic.Error, ApiFile));
    }

    [Fact]
    public void Run_EditorConfigCoveringManyFiles_ReportsOncePerDirectory()
    {
        // One .editorconfig applies to every file under its directory, so reporting per syntax
        // tree would emit one BW0016 per file in the project.
        var diagnostics = ScanCompilation(
            LoweringSource.EditorConfig,
            ReportDiagnostic.Warn,
            "/repo/src/Api/Consumer.cs",
            "/repo/src/Api/Other.cs",
            "/repo/src/Core/Service.cs");

        Assert.Equal(
            ["an .editorconfig applying to 'src/Api/'", "an .editorconfig applying to 'src/Core/'"],
            diagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture).Split(" by ")[1].Split(';')[0]));
    }

    [Fact]
    public async Task EditorConfigLoweringARatchetId_ReportsSeverityLowered()
    {
        await AnalyzerOnSampleProject()
            .WithAnalyzerConfig(ApiEditorConfig, "[*.cs]\ndotnet_diagnostic.BW0005.severity = warning\n")
            .Expect(new DiagnosticResult(DiagnosticDescriptors.SeverityLowered)
                .WithArguments("BW0005", "warn", "an .editorconfig applying to 'src/Api/'"))
            .RunAsync();
    }

    [Fact]
    public async Task ExpiryWarningMayBeLowered_WithoutSeverityLowered()
    {
        await AnalyzerOnSampleProject()
            .WithAnalyzerConfig(ApiEditorConfig, "[*.cs]\ndotnet_diagnostic.BW0011.severity = suggestion\n")
            .RunAsync();
    }

    private static AnalyzerHarness AnalyzerOnSampleProject() =>
        AnalyzerHarness.WithBaseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite))
            .WithConsumer(SampleProjectFixture.Consumer);

    private static IEnumerable<Diagnostic> ScanCompilation(LoweringSource source, ReportDiagnostic report, params string[] paths)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithSyntaxTreeOptionsProvider(new OptionsProvider(
                global: source == LoweringSource.GlobalAnalyzerConfig ? report : null,
                perTree: source == LoweringSource.EditorConfig ? report : null));

        if (source == LoweringSource.CompilerOptions)
        {
            options = options.WithSpecificDiagnosticOptions(
                System.Collections.Immutable.ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(RatchetId, report));
        }

        var compilation = CSharpCompilation.Create(
            SampleProjectFixture.Project,
            paths.Select(path => CSharpSyntaxTree.ParseText(SourceText.From(string.Empty), path: path)),
            references: null,
            options);

        return DiagnosticSeverityScanner.Run(compilation, SampleProjectFixture.RepoRoot, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Stands in for the analyzer config the compiler builds from .globalconfig and .editorconfig
    /// files. Applies to every tree, so a per-directory report has to come from the validator.
    /// </summary>
    private sealed class OptionsProvider(ReportDiagnostic? global, ReportDiagnostic? perTree) : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => GeneratedKind.Unknown;

        public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = global ?? default;
            return global is not null && diagnosticId == RatchetId;
        }

        public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
        {
            severity = perTree ?? default;
            return perTree is not null && diagnosticId == RatchetId;
        }
    }
}
