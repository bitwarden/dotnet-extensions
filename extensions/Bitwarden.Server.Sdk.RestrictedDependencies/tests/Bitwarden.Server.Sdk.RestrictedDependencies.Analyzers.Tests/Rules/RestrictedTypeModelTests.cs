using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Rules;

/// <summary>
/// BW0015 for settings the attribute itself cannot mean. The diagnostic sits on the attribute, and
/// every case is a build failure rather than a lowered guarantee.
/// </summary>
public class RestrictedTypeModelTests
{
    private const string WidgetPath = "/repo/src/Core/IWidget.cs";

    private const string ForbiddenExistingButAllowedNew = """
        using Bitwarden.Server.Sdk.RestrictedDependencies;

        namespace Test;

        [{|BW0015:RestrictedDependency(AllowExistingUses = false, AllowNewUses = true)|}]
        public interface IWidget
        {
        }
        """;

    private const string InvalidAllowedPathGlob = """
        using Bitwarden.Server.Sdk.RestrictedDependencies;

        namespace Test;

        [{|BW0015:RestrictedDependency(AllowedPaths = new[] { @"src\Core\**" })|}]
        public interface IWidget
        {
        }
        """;

    private const string TypeLevelSettingOnAMember = """
        using Bitwarden.Server.Sdk.RestrictedDependencies;

        namespace Test;

        [RestrictedDependency]
        public interface IWidget
        {
            [{|BW0015:RestrictedDependency(SealMembers = true)|}]
            void Spin();
        }
        """;

    [Theory]
    [InlineData(ForbiddenExistingButAllowedNew)]
    [InlineData(InvalidAllowedPathGlob)]
    [InlineData(TypeLevelSettingOnAMember)]
    public async Task SettingsTheAttributeCannotMean_ReportsAttributeInvalidOnTheAttribute([StringSyntax("C#-test")] string widget)
    {
        // An empty baseline is supplied so the only BW0015 in play is the attribute's own.
        await new AnalyzerHarness()
            .WithBaselineJson(new BudgetModel("Test.IWidget", [], []).Serialize(), "Test.IWidget")
            .WithSource(WidgetPath, widget)
            .RunAsync();
    }

    /// <summary>
    /// A member attribute is only ever read off a type that carries the attribute itself, so one
    /// on an unmarked type's member governs nothing — even though the attribute's own
    /// documentation offers it for "a type, or one of its members".
    /// </summary>
    [Fact]
    public async Task MemberAttributeOnAnUnmarkedType_ReportsAttributeInvalidOnTheAttribute()
    {
        await AnalyzerHarness.WithBaseline()
            .WithSource(WidgetPath, """
                using Bitwarden.Server.Sdk.RestrictedDependencies;

                namespace Test;

                public interface IWidget
                {
                    [{|BW0015:RestrictedDependency(Tracking = "PM-2")|}]
                    void Spin();
                }
                """)
            .RunAsync();
    }

    /// <summary>
    /// GetMembers returns nested types beside the members. A nested type that carries the attribute
    /// is a restricted type of its own, so a type-level setting on it is not a member override and
    /// reports nothing.
    /// </summary>
    [Fact]
    public async Task TypeLevelSettingOnANestedRestrictedType_ReportsNothing()
    {
        await new AnalyzerHarness()
            .WithBaselineJson(new BudgetModel("Test.IWidget", [], []).Serialize(), "Test.IWidget")
            .WithBaselineJson(new BudgetModel("Test.IWidget+Options", [], []).Serialize(), "Test.IWidget+Options")
            .WithSource(WidgetPath, """
                using Bitwarden.Server.Sdk.RestrictedDependencies;

                namespace Test;

                [RestrictedDependency]
                public interface IWidget
                {
                    [RestrictedDependency(SealMembers = true)]
                    public interface Options
                    {
                    }
                }
                """)
            .RunAsync();
    }
}
