using Microsoft.Build.Utilities.ProjectCreation;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkTests : MSBuildTestBase
{
    [Fact]
    public void NoOverridingProperties_CanCompile()
    {
        ProjectCreator.Templates.SdkProject()
            .TryBuild(restore: true, out var result, out var buildOutput)
            .TryGetConstant("BIT_INCLUDE_LOGGING", out var hasLoggingConstant)
            .TryGetConstant("BIT_INCLUDE_TELEMETRY", out var hasTelementryConstant)
            .TryGetConstant("BIT_INCLUDE_FEATURES", out var hasFeaturesConstant);

        Assert.True(result, buildOutput.GetConsoleLog());

        Assert.True(hasLoggingConstant);
        Assert.True(hasTelementryConstant);
        Assert.True(hasFeaturesConstant);
    }

    [Fact]
    public void LoggingTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            customAction: (project) =>
            {
                project.Property("BitIncludeLogging", bool.FalseString);
            }
        )
            .TryBuild(restore: true, out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void TelemetryTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            customAction: (project) =>
            {
                project.Property("BitIncludeTelemetry", bool.FalseString);
            }
        )
            .TryBuild(restore: true, out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.FalseString);
            }
        )
            .TryBuild(restore: true, out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanNotUseFeatureService()
    {
        ProjectCreator.Templates.SdkProject(
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.FalseString);
            },
            additional: """
                app.MapGet("/test", (Bitwarden.Server.Sdk.Features.IFeatureService featureService) => featureService.GetAll());
                """
        )
            .TryBuild(restore: true, out var result, out var buildOutput);

        Assert.False(result, buildOutput.GetConsoleLog());

        // error CS0234: The type or namespace name 'Features' does not exist in the namespace 'Bitwarden.Server.Sdk' (are you missing an assembly reference?)
        Assert.Contains(buildOutput.ErrorEvents, e => e.Code == "CS0234");
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
            customAction: (project) =>
            {
                project.Property("BitIncludeLogging", includeLogging.ToString());
                project.Property("BitIncludeTelemetry", includeTelemetry.ToString());
                project.Property("BitIncludeFeatures", includeFeatures.ToString());
            }
        )
            .TryBuild(restore: true, out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());
    }
}
