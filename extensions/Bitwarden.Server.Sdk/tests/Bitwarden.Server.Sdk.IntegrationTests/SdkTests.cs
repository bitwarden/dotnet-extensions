using Microsoft.Build.Utilities.ProjectCreation;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkTests : MSBuildTestBase
{
    [Fact]
    public void NoOverridingProperties_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(out var result, out var buildOutput)
            .TryGetConstant("BIT_INCLUDE_TELEMETRY", out var hasTelementryConstant)
            .TryGetConstant("BIT_INCLUDE_FEATURES", out var hasFeaturesConstant);

        Assert.True(result, buildOutput.GetConsoleLog());

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

        // error CS0234: The type or namespace name 'Features' does not exist in the namespace 'Bitwarden.Server.Sdk' (are you missing an assembly reference?)
        Assert.Contains(buildOutput.ErrorEvents, e => e.Code == "CS0234");
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
    public void AllVariants_Work(bool includeTelemetry, bool includeFeatures, bool includeAuthentication)
    {
        if (ExcludedVariants.Contains((includeTelemetry, includeFeatures, includeAuthentication)))
        {
            Assert.Skip($"""
                Excluded Variant Skipped:
                    IncludeTelemetry = {includeTelemetry}
                    IncludeFeatures = {includeFeatures}
                    IncludeAuthentication = {includeAuthentication}
                """);
        }

        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeTelemetry", includeTelemetry.ToString());
                project.Property("BitIncludeFeatures", includeFeatures.ToString());
                project.Property("BitIncludeAuthentication", includeAuthentication.ToString());
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }
}

internal class XUnitLoggerProvider : ILoggerProvider
{
    private class XUnitLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (TestContext.Current.TestOutputHelper == null)
            {
                return;
            }

            TestContext.Current.TestOutputHelper.WriteLine($"[{category}]: {formatter(state, exception)}");
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(categoryName);
    }

    public void Dispose() { }
}
