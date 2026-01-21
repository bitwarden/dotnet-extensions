using System.Diagnostics;
using Microsoft.Build.Utilities.ProjectCreation;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkTests : MSBuildTestBase
{
    [Fact]
    public void NoOverridingProperties_CanCompile()
    {
        IEnumerable<(string Feature, bool DefaultValue)> featuresAndDefaults = [
            ("TELEMETRY", true),
            ("FEATURES", true),
            ("AUTHENTICATION", true),
            ("CACHING", false),
        ];

        var project = ProjectCreator.Templates.SdkProject(out var result, out var buildOutput);

        Assert.True(result, buildOutput.GetConsoleLog());

        foreach (var (feature, expectedDefault) in featuresAndDefaults)
        {
            project.TryGetConstant($"BIT_INCLUDE_{feature}", out var actualValue);
            Assert.Equal(expectedDefault, actualValue);
        }
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
    public void ShouldBuildWithNoDocsWarnings()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("TreatWarningsAsErrors", "true");
                project.Property("GenerateDocumentationFile", "true");
            }
        );

        Assert.Empty(buildOutput.Errors);
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

    [Fact]
    public void CachingTurnedOn_CanUseFusionCache()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeCaching", bool.TrueString);
            },
            additional: """
                app.MapGet("/test", ([FromKeyedServices("Test")]  ZiggyCreatures.Caching.Fusion.IFusionCache cache) => cache.GetOrSetAsync("Key", true));
                """
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void AuthenticationTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeAuthentication", bool.FalseString);
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    public static TheoryData<string> PossibleVariantData()
    {
        string[] features = ["Telemetry", "Features", "Authentication", "Caching"];
        var totalCombinations = (int)Math.Pow(2, features.Length);

        var theory = new TheoryData<string>();

        for (var i = 0; i < totalCombinations; i++)
        {
            var variant = new Dictionary<string, bool>();

            for (var j = 0; j < features.Length; j++)
            {
                // Check if the j-th bit is set in i
                variant[features[j]] = (i & (1 << j)) != 0;
            }

            // TODO: When there are variants that need to be skipped do so here but still add
            // a row with a skip message
            theory.Add(Serialize(variant));
        }

        return theory;

        // We serialize it into a simple string so that it can be easily viewed in test explorer
        static string Serialize(Dictionary<string, bool> features)
        {
            return string.Join(',', features.Select(feature => $"{feature.Key}={feature.Value}"));
        }
    }

    [Theory, MemberData(nameof(PossibleVariantData))]
    public void AllVariants_Work(string featureSets)
    {
        // Deserialize from simple string into dictionary
        var features = featureSets.Split(",")
            .Select(featureSet =>
            {
                var split = featureSet.Split("=");
                Debug.Assert(split.Length == 2, "Invalid format");
                return new KeyValuePair<string, bool>(split[0], bool.Parse(split[1]));
            })
            .ToDictionary();

        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                foreach (var feature in features)
                {
                    project.Property($"BitInclude{feature.Key}", feature.Value.ToString());
                }
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }
}

internal sealed class XUnitLoggerProvider : ILoggerProvider
{
    private sealed class XUnitLogger(string category) : ILogger
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
