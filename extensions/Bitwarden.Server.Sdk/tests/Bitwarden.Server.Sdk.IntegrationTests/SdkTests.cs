using System.Collections.ObjectModel;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Utilities.ProjectCreation;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkTests : MSBuildTestBase
{
    [Fact]
    public void NoOverridingProperties_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(out var result, out var buildOutput)
            .TryGetConstant("BIT_INCLUDE_LOGGING", out var hasLoggingConstant)
            .TryGetConstant("BIT_INCLUDE_TELEMETRY", out var hasTelementryConstant)
            .TryGetConstant("BIT_INCLUDE_FEATURES", out var hasFeaturesConstant);

        Assert.True(result, buildOutput.GetConsoleLog());

        Assert.True(hasLoggingConstant);
        Assert.True(hasTelementryConstant);
        Assert.True(hasFeaturesConstant);
    }

    [Fact]
    public void ShouldBuildWithNoWarningsIfProjectHasNullableDisabled()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("Nullable", "disable");
            }
        )
            .TryGetItems("Compile", out var compileItems);

        Assert.True(result, buildOutput.GetConsoleLog());

        Assert.Empty(buildOutput.WarningEvents);
    }

    [Fact]
    public void LoggingTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeLogging", bool.FalseString);
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void TelemetryTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeTelemetry", bool.FalseString);
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.FalseString);
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanNotUseFeatureService()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.FalseString);
            },
            additional: """
                app.MapGet("/test", (Bitwarden.Server.Sdk.Features.IFeatureService featureService) => featureService.GetAll());
                """
        );

        Assert.False(result, buildOutput.GetConsoleLog());

        // error CS0246: The type or namespace name 'Bitwarden' could not be found (are you missing a using directive or an assembly reference?)
        Assert.Contains(buildOutput.ErrorEvents, e => e.Code == "CS0246");
    }

    [Fact]
    public void FeaturesTurnedOn_CanUseFeatureService()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.TrueString);
            },
            additional: """
                app.MapGet("/test", (Bitwarden.Server.Sdk.Features.IFeatureService featureService) => featureService.GetAll());
                """
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    public static TheoryData<bool, bool, bool> MatrixData
        => new MatrixTheoryData<bool, bool, bool>([true, false], [true, false], [true, false]);

    // There will be some variants that disallow the use of feature Y if feature X is not also enabled.
    // Use this set to exclude those known variants from being tested.
    public static HashSet<(bool, bool, bool)> ExcludedVariants => [];

    [Theory, MemberData(nameof(MatrixData))]
    public void AllVariants_Work(bool includeLogging, bool includeTelemetry, bool includeFeatures)
    {
        if (ExcludedVariants.Contains((includeLogging, includeTelemetry, includeFeatures)))
        {
            Assert.Skip($"""
                Excluded Variant Skipped:
                    IncludeLogging = {includeLogging}
                    IncludeTelemetry = {includeTelemetry}
                    IncludeFeatures = {includeFeatures}
                """);
        }

        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeLogging", includeLogging.ToString());
                project.Property("BitIncludeTelemetry", includeTelemetry.ToString());
                project.Property("BitIncludeFeatures", includeFeatures.ToString());
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }
}
