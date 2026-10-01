using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

/// <summary>
/// BW0014, plus the BW0015 that fires when a restricted type has no baseline at all. A sealed type
/// growing past what its baseline records is reported at the new member; a restricted type the
/// build supplied no usable baseline for is reported at its declaration.
/// </summary>
public class DeclaredMemberSetCheckerTests
{
    /// <summary>
    /// What the build supplied in place of a usable baseline for the restricted type.
    /// </summary>
    public enum BaselineSupply
    {
        /// <summary>
        /// Nothing: the type carries the attribute and no baseline was supplied at all.
        /// </summary>
        Nothing,

        /// <summary>
        /// An AdditionalFile the build did not mark as a baseline. Unmarked metadata reads as empty
        /// rather than absent, so a presence check would read every additional file as a baseline.
        /// </summary>
        UnmarkedAdditionalFile,

        /// <summary>
        /// A configured regenerate command, which the diagnostic quotes back in place of the generic
        /// instruction.
        /// </summary>
        UpdateCommand,

        /// <summary>
        /// A marked baseline that does not parse. The file is described on its own, and the type still
        /// reads as having no baseline.
        /// </summary>
        MalformedBaseline,
    }

    private const string SettingsPath = "/repo/src/Core/Settings/GlobalSettings.cs";

    [Theory]
    [InlineData(BaselineSupply.Nothing, "regenerate the committed baselines")]
    [InlineData(BaselineSupply.UnmarkedAdditionalFile, "regenerate the committed baselines")]
    [InlineData(BaselineSupply.UpdateCommand, "run `dotnet run --project util/Analyzers/RestrictedDependencies.Tool -- update`")]
    [InlineData(BaselineSupply.MalformedBaseline, "regenerate the committed baselines")]
    public async Task NoUsableBaseline_ReportsAttributeInvalidAtTheTypeDeclaration(BaselineSupply supply, string instruction)
    {
        var harness = new AnalyzerHarness().WithSource(SampleProjectFixture.ServicePath, SampleProjectFixture.ServiceWithMarkedInterface);

        switch (supply)
        {
            case BaselineSupply.UnmarkedAdditionalFile:
                harness.WithUnmarkedAdditionalFile("/repo/src/Core/notes.json", "this is not a baseline");
                break;
            case BaselineSupply.UpdateCommand:
                harness.WithUpdateCommand("dotnet run --project util/Analyzers/RestrictedDependencies.Tool -- update");
                break;
            case BaselineSupply.MalformedBaseline:
                harness
                    .WithBaselineJson($"{{ \"type\": \"{SampleProjectFixture.Type}\", \"sites\": 3 }}")
                    .Expect(new DiagnosticResult(DiagnosticDescriptors.AttributeInvalid)
                        .WithArguments($"Baseline 'baselines/{SampleProjectFixture.Type}.json' is malformed: 'declaredMembers' must be an array."));
                break;
        }

        await harness
            .Expect(new DiagnosticResult(DiagnosticDescriptors.AttributeInvalid)
                .WithLocation(0)
                .WithArguments($"'IUserService' carries [RestrictedDependency] but no baseline named '{SampleProjectFixture.Type}.json' was supplied; {instruction}."))
            .RunAsync();
    }

    [Fact]
    public async Task SealedInterface_GainsMemberNotInBaseline_ReportsGrowth()
    {
        await new AnalyzerHarness()
            .WithBaselineJson(SampleProjectFixture.Baseline())
            .WithSource(SampleProjectFixture.ServicePath, SampleProjectFixture.Service
                .Replace(
                    "Task<bool> CanAccessPremium(User user);",
                    "Task<bool> CanAccessPremium(User user);\n    Task {|BW0014:BrandNew|}(User user);")
                .Replace(
                    "public static bool IsLegacyUser(User user) => false;",
                    "public Task BrandNew(User user) => Task.CompletedTask;\n    public static bool IsLegacyUser(User user) => false;"))
            .RunAsync();
    }

    [Fact]
    public async Task SealedClass_GainsNestedTypeOrProperty_ReportsGrowth()
    {
        var baseline = new BudgetModel(
            "Test.GlobalSettings",
            ["P:Test.GlobalSettings.SelfHosted", "T:Test.GlobalSettings.SqlSettings", "P:Test.GlobalSettings.SqlSettings.ConnectionString"],
            []).Serialize();

        await new AnalyzerHarness()
            .WithBaselineJson(baseline, "Test.GlobalSettings")
            .WithSource(SettingsPath, """
                using Bitwarden.Server.Sdk.RestrictedDependencies;

                namespace Test;

                [RestrictedDependency(AllowExistingUses = true, AllowNewUses = true, SealNestedTypes = true, SealProperties = true)]
                public class GlobalSettings
                {
                    public bool SelfHosted { get; set; }
                    public string {|BW0014:NewTopLevel|} { get; set; } = string.Empty;
                    public SqlSettings {|#0:Sql|} { get; set; } = new();

                    public class SqlSettings
                    {
                        public string ConnectionString { get; set; } = string.Empty;
                        public int {|BW0014:NewNested|} { get; set; }
                    }

                    public class {|BW0014:MailSettings|}
                    {
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.SealedTypeGrew)
                .WithLocation(0)
                .WithArguments("GlobalSettings", "untracked", "Sql"))
            .RunAsync();
    }
}
