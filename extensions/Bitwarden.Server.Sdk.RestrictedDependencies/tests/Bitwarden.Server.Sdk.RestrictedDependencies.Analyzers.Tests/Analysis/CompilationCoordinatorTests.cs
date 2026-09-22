using System.Globalization;
using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

/// <summary>
/// The startup checks the coordinator runs once per compilation, and the mode it routes the end
/// of the compilation by: enforce reports excess and stale uses, observe reports rows.
/// </summary>
public class CompilationCoordinatorTests
{
    [Fact]
    public async Task OrphanedBaseline_ReportsAttributeInvalidWithoutLocation()
    {
        // There is no attribute left to point at, so the diagnostic carries no location.
        await new AnalyzerHarness()
            .WithBaselineJson(new BudgetModel("Test.IWidget", [], []).Serialize(), "Test.IWidget")
            .WithSource("/repo/src/Core/IWidget.cs", """
                namespace Test;

                public interface IWidget
                {
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.AttributeInvalid)
                .WithArguments("Baseline 'baselines/Test.IWidget.json' is orphaned: 'Test.IWidget' no longer carries [RestrictedDependency]. Delete the file or restore the attribute."))
            .RunAsync();
    }

    /// <summary>
    /// Observe mode takes both halves of a baseline tool's wiring. A seed list alone — the half an
    /// .editorconfig can reach, since it cannot touch CompilationOptions.SpecificDiagnosticOptions
    /// — leaves the build enforcing and is reported, so it cannot be used to stand a project down.
    /// </summary>
    [Fact]
    public async Task SeedTypesWithoutTheObservationChannel_StaysEnforcing_AndReportsAttributeInvalid()
    {
        var diagnostics = await ToolHost.RunAsync(observe: true, enableObservations: false);

        Assert.Contains(
            diagnostics,
            d => d.Id == DiagnosticDescriptors.AttributeInvalid.Id
                && d.GetMessage(CultureInfo.InvariantCulture).Contains(AnalyzerConfigConstants.SeedTypes, StringComparison.Ordinal)
                && d.GetMessage(CultureInfo.InvariantCulture).Contains("stays enforcing", StringComparison.Ordinal));
    }

    /// <summary>
    /// "Stays enforcing" has to mean it still gates, not just that it says so. The seed here names
    /// a type this compilation does not restrict: a seed read off the key would make that the whole
    /// restricted-type set and let the real violation through, while one read off the mode ignores
    /// it and keeps the committed baselines as the authority.
    /// </summary>
    [Fact]
    public async Task SeedTypesWithoutTheObservationChannel_StillGatesAgainstTheCommittedBaselines()
    {
        var diagnostics = await ToolHost.RunAsync(
            observe: true,
            enableObservations: false,
            seedTypes: "Test.IWidget",
            baselineJson: SampleProjectFixture.Baseline());

        // The committed baseline records no uses, so the sample consumer's injection is over
        // budget. Seeding off the key would have left IUserService unrestricted and silent.
        Assert.Contains(diagnostics, d => d.Id == DiagnosticConstants.Injection);
    }

    /// <summary>
    /// Analysis on with no baseline supplied gates nothing, so the project that consumes a
    /// restricted type without one is told rather than silently exempted.
    /// </summary>
    [Fact]
    public async Task AnalysisEnabled_WithNoBaselineSupplied_ReportsAttributeInvalid()
    {
        await new AnalyzerHarness()
            .WithConsumer("""
                namespace Test;

                public class Consumer
                {
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.AttributeInvalid)
                .WithArguments($"Project '{SampleProjectFixture.Project}' has RestrictedDependencyAnalysis enabled but no baseline was supplied, so nothing is gated; set RestrictedDependencyBaselinesPath for this project, or turn RestrictedDependencyAnalysis off for it."))
            .RunAsync();
    }

    /// <summary>
    /// The malformed baseline already says what is wrong, and a project that declares no restricted
    /// type of its own gets no per-type diagnostic to say it either. The no-baseline check must not
    /// pile a second, misleading BW0015 on top: a baseline was supplied here, it just did not parse.
    /// </summary>
    [Fact]
    public async Task MalformedBaseline_InAConsumingProject_ReportsOnlyTheMalformedBaseline()
    {
        await new AnalyzerHarness()
            .WithBaselineJson($"{{ \"type\": \"{SampleProjectFixture.Type}\", \"sites\": 3 }}")
            .WithConsumer("""
                namespace Test;

                public class Consumer
                {
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.AttributeInvalid)
                .WithArguments($"Baseline 'baselines/{SampleProjectFixture.Type}.json' is malformed: 'declaredMembers' must be an array."))
            .RunAsync();
    }

    [Fact]
    public async Task InEnforceMode_NoObservationsAreBuilt()
    {
        // Enforce mode is every real build. Even with the channel turned on it stays silent, so
        // the rows cost nothing to produce when nobody is reading them.
        var diagnostics = await ToolHost.RunAsync(observe: false, enableObservations: true);

        Assert.Empty(ToolHost.Rows(diagnostics, ObservationConstants.UsageRow));
    }
}
