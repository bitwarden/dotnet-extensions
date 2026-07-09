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
        var variations = new Dictionary<string, string[]>
        {
            { "BitIncludeTelemetry", ["true", "false"] },
            { "BitIncludeFeatures", ["true", "false"] },
            { "BitIncludeAuthentication", ["true", "false"] },
            { "BitIncludeCaching", ["true", "false"] },
            { "BitAspireIntegration", ["enabled", "disabled"] },
        };

        var keys = variations.Keys.ToArray();
        var theory = new TheoryData<string>();

        // Generate the cartesian product of all possible values for each property
        IEnumerable<IEnumerable<string>> seed = [[]];
        var combinations = variations.Values.Aggregate(seed, (acc, values) =>
            from prev in acc
            from value in values
            select prev.Append(value));

        foreach (var combination in combinations)
        {
            var variant = keys.Zip(combination).ToDictionary(kv => kv.First, kv => kv.Second);

            // TODO: When there are variants that need to be skipped do so here but still add
            // a row with a skip message
            theory.Add(Serialize(variant));
        }

        return theory;

        // We serialize it into a simple string so that it can be easily viewed in test explorer
        static string Serialize(Dictionary<string, string> properties)
        {
            return string.Join(',', properties.Select(p => $"{p.Key}={p.Value}"));
        }
    }

    [Theory, MemberData(nameof(PossibleVariantData))]
    public void AllVariants_Work(string featureSets)
    {
        // Deserialize from simple string into dictionary
        var properties = featureSets.Split(",")
            .Select(featureSet =>
            {
                var split = featureSet.Split("=");
                Debug.Assert(split.Length == 2, "Invalid format");
                return new KeyValuePair<string, string>(split[0], split[1]);
            })
            .ToDictionary();

        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                foreach (var property in properties)
                {
                    project.Property(property.Key, property.Value);
                }
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
