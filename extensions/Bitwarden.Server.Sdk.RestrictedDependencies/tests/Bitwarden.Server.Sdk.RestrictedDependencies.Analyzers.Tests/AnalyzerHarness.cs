using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests;

/// <summary>
/// Configures the analyzer test harness the way a consuming repository's build configures a
/// production project: RestrictedDependencyAnalysis and RepoRoot as global analyzer config, the
/// generated attribute source compiled in, and baselines supplied as marked additional files.
/// </summary>
internal sealed class AnalyzerHarness : CSharpAnalyzerTest<RestrictedDependencyAnalyzer, DefaultVerifier>
{
    /// <summary>
    /// <paramref name="seedTypes"/> goes into the same global config as the rest of the build
    /// properties, which is where a build puts it. It cannot be added as a second global config
    /// file: a further <c>is_global</c> file makes the keys in this one unreadable, and the
    /// analyzer then reports nothing at all.
    /// </summary>
    public AnalyzerHarness(bool enabled = true, string repoRoot = SampleProjectFixture.RepoRoot, string? seedTypes = null)
    {
        ReferenceAssemblies = ReferenceAssemblies.Net.Net100;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location));
        // The harness's suppression check prepends "#pragma warning disable <id>" to every source
        // and expects the diagnostic to vanish. BW0012 exists to flag exactly that, so skip it.
        TestBehaviors |= TestBehaviors.SkipSuppressionCheck;
        TestState.Sources.Add(("/repo/src/Core/RestrictedDependencyAttributes.g.cs", AttributeConstants.Text));
        var seed = seedTypes is null ? string.Empty : $"{AnalyzerConfigConstants.SeedTypes} = {seedTypes}\n";
        TestState.AnalyzerConfigFiles.Add(("/.globalconfig",
            $"is_global = true\n{AnalyzerConfigConstants.Analysis} = {(enabled ? "true" : "false")}\n{AnalyzerConfigConstants.RepositoryRoot} = {repoRoot}\n{seed}"));
    }

    /// <summary>
    /// A test with the fixture service compiled in and a baseline holding <paramref name="usages"/>.
    /// </summary>
    public static AnalyzerHarness WithBaseline(params BudgetEntry[] usages) =>
        new AnalyzerHarness().WithService().WithBaselineJson(SampleProjectFixture.Baseline(usages));

    public AnalyzerHarness WithService() => WithSource(SampleProjectFixture.ServicePath, SampleProjectFixture.Service);

    public AnalyzerHarness WithSource(string path, string source)
    {
        TestState.Sources.Add((path, source));
        return this;
    }

    public AnalyzerHarness WithConsumer(string source) => WithSource(SampleProjectFixture.ConsumerPath, source);

    /// <summary>
    /// Adds a baseline, marked the way the package's build props mark one. The directory is
    /// arbitrary on purpose: the analyzer recognizes a baseline by that metadata, not by location.
    /// </summary>
    public AnalyzerHarness WithBaselineJson(string json, string type = SampleProjectFixture.Type)
    {
        var path = $"/repo/baselines/{type}.json";
        TestState.AdditionalFiles.Add((path, json));
        TestState.AnalyzerConfigFiles.Add(($"/baseline.{type}.globalconfig",
            $"is_global = true\n\n[{path}]\n{AnalyzerConfigConstants.BaselineMetadata} = true\n"));
        return this;
    }

    /// <summary>
    /// Adds an additional file the build did not mark as a baseline, to prove the analyzer
    /// ignores it.
    /// </summary>
    public AnalyzerHarness WithUnmarkedAdditionalFile(string path, string content)
    {
        TestState.AdditionalFiles.Add((path, content));
        return this;
    }

    /// <summary>
    /// Sets the regenerate command the consuming repository configures, which BW0013 and BW0015
    /// quote back.
    /// </summary>
    public AnalyzerHarness WithUpdateCommand(string command)
    {
        TestState.AnalyzerConfigFiles.Add(("/update-command.globalconfig",
            $"is_global = true\n{AnalyzerConfigConstants.UpdateCommand} = {command}\n"));
        return this;
    }

    public AnalyzerHarness WithAnalyzerConfig(string path, string content)
    {
        TestState.AnalyzerConfigFiles.Add((path, content));
        return this;
    }

    public AnalyzerHarness Expect(DiagnosticResult result)
    {
        ExpectedDiagnostics.Add(result);
        return this;
    }

    /// <summary>
    /// Runs with the test's own cancellation token, so no call site has to pass one.
    /// </summary>
    public Task RunAsync() => RunAsync(TestContext.Current.CancellationToken);
}
