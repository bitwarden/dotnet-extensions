using System.Collections.Immutable;
using Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

/// <summary>
/// How the index reads the committed baselines: a document it cannot use is skipped and described
/// as BW0015, and a second document for a type already loaded is skipped in favour of the first.
/// The ordering cases drive loading directly rather than through the analyzer harness because they
/// depend on the order the build lists the additional files in, and the harness does not promise one.
/// </summary>
public class BudgetIndexTests
{

    [Fact]
    public void SecondBaselineForTheSameType_IsSkippedAndDescribed()
    {
        var index = Load(out var problems,
            new MarkedFile("/repo/baselines/Test.IUserService.json", SampleProjectFixture.Baseline()),
            new MarkedFile("/repo/other/Test.IUserService.json", SampleProjectFixture.Baseline()));

        Assert.Equal([$"Baseline 'other/{SampleProjectFixture.Type}.json' duplicates the baseline for '{SampleProjectFixture.Type}'."], problems);
        Assert.True(index.TryGetPath(SampleProjectFixture.Type, out var path));
        Assert.Equal($"baselines/{SampleProjectFixture.Type}.json", path);
    }

    [Fact]
    public void UnreadableBaseline_IsSkippedAndDescribed()
    {
        var index = Load(out var problems, new MarkedFile("/repo/baselines/Test.IUserService.json", text: null));

        Assert.Equal([$"Baseline 'baselines/{SampleProjectFixture.Type}.json' could not be read."], problems);
        Assert.Empty(index.Types);
    }

    [Fact]
    public async Task DuplicateRowsForOneKey_AreRejectedRatherThanSummedIntoABiggerBudget()
    {
        // Two rows of count 2 would budget four uses, while three uses still satisfied each row on
        // its own - so nothing would report the slack and the ratchet could never be turned down.
        // The document is rejected instead, which surfaces as BW0015 and fails the build.
        var duplicated = SampleProjectFixture.MemberSite(SampleProjectFixture.CanAccessPremium, SampleProjectFixture.RunSite, count: 2);

        await new AnalyzerHarness()
            .WithSource(SampleProjectFixture.ServicePath, SampleProjectFixture.ServiceWithMarkedInterface)
            .WithBaselineJson(SampleProjectFixture.Baseline(
                SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite),
                duplicated,
                duplicated))
            .WithConsumer(SampleProjectFixture.ConsumerPreamble + """
                    public async Task Run(User user)
                    {
                        await _userService.CanAccessPremium(user);
                        await _userService.CanAccessPremium(user);
                        await _userService.CanAccessPremium(user);
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.AttributeInvalid)
                .WithArguments($"Baseline 'baselines/{SampleProjectFixture.Type}.json' is malformed: Duplicate 'member' site '{SampleProjectFixture.RunSite}' in project '{SampleProjectFixture.Project}'."))
            .Expect(new DiagnosticResult(DiagnosticDescriptors.AttributeInvalid)
                .WithLocation(0)
                .WithArguments($"'IUserService' carries [RestrictedDependency] but no baseline named '{SampleProjectFixture.Type}.json' was supplied; regenerate the committed baselines."))
            .RunAsync();
    }

    /// <summary>
    /// Loads every supplied file as a baseline, the way the package's build props mark one.
    /// </summary>
    private static BudgetIndex Load(out ImmutableArray<string> problems, params MarkedFile[] files)
    {
        var allMarkedAsBaselines = new TestAnalyzerConfigOptionsProvider(
            globalOptions: new Dictionary<string, string>(),
            fileOptions: new Dictionary<string, string> { [AnalyzerConfigConstants.BaselineMetadata] = "true" });

        var options = new AnalyzerOptions([.. files], allMarkedAsBaselines);
        return BudgetIndex.Load(options, SampleProjectFixture.RepoRoot, TestContext.Current.CancellationToken, out problems);
    }

    private sealed class MarkedFile(string path, string? text) : AdditionalText
    {
        public override string Path => path;

        public override SourceText? GetText(CancellationToken cancellationToken = default) =>
            text is null ? null : SourceText.From(text);
    }

}
