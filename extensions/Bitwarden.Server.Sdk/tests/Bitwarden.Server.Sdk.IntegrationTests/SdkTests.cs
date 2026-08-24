using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkTests
{
    [Fact]
    public void NoOverridingProperties_CanCompile()
    {
        IEnumerable<(string Feature, bool DefaultValue)> featuresAndDefaults = [
            ("TELEMETRY", true),
            ("FEATURES", true),
            ("AUTHENTICATION", true),
            ("CACHING", false),
            ("WEB_ESSENTIALS", true),
            ("ENVIRONMENT", true),
        ];

        using var project = new TempDotNetProject();
        project.WithDefaultProgramCs();

        var result = project.Build();
        Assert.True(result.Succeeded, result.GetConsoleLog());

        foreach (var (feature, expectedDefault) in featuresAndDefaults)
            Assert.Equal(expectedDefault, project.HasConstant($"BIT_INCLUDE_{feature}"));
    }

    [Fact]
    public void LibraryProject_DefaultsEntryPointFeaturesOff()
    {
        using var project = new TempDotNetProject("Microsoft.NET.Sdk");

        foreach (var feature in new[] { "TELEMETRY", "AUTHENTICATION", "WEB_ESSENTIALS" })
            Assert.False(project.HasConstant($"BIT_INCLUDE_{feature}"));

        Assert.True(project.HasConstant("BIT_INCLUDE_FEATURES"));
        Assert.True(project.HasConstant("BIT_INCLUDE_ENVIRONMENT"));
    }

    [Fact]
    public void ShouldBuildWithNoWarningsIfProjectHasNullableDisabled()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("Nullable", "disable");
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
        Assert.Empty(result.WarningEvents);
    }

    [Fact]
    public void ShouldBuildWithNoDocsWarnings()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("TreatWarningsAsErrors", "true");
        project.WithProperty("GenerateDocumentationFile", "true");
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TelemetryTurnedOff_CanCompile()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeTelemetry", bool.FalseString);
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanCompile()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeFeatures", bool.FalseString);
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanNotUseFeatureService()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeFeatures", bool.FalseString);
        project.WithDefaultProgramCs("""
            app.MapGet("/test", (Bitwarden.Server.Sdk.Features.IFeatureService featureService) => featureService.GetAll());
            """);

        var result = project.Build();

        Assert.False(result.Succeeded, result.GetConsoleLog());

        // error CS0234: The type or namespace name 'Features' does not exist in the namespace 'Bitwarden.Server.Sdk' (are you missing an assembly reference?)
        Assert.Contains(result.ErrorEvents, e => e.Code == "CS0234");
    }

    [Fact]
    public void FeaturesTurnedOn_CanUseFeatureService()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeFeatures", bool.TrueString);
        project.WithDefaultProgramCs("""
            app.MapGet("/test", (Bitwarden.Server.Sdk.Features.IFeatureService featureService) => featureService.GetAll());
            """);

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
    }

    [Fact]
    public void CachingTurnedOn_CanUseFusionCache()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeCaching", bool.TrueString);
        project.WithDefaultProgramCs("""
            app.MapGet("/test", ([FromKeyedServices("Test")]  ZiggyCreatures.Caching.Fusion.IFusionCache cache) => cache.GetOrSetAsync("Key", true));
            """);

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
    }

    [Fact]
    public void AuthenticationTurnedOff_CanCompile()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeAuthentication", bool.FalseString);
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
    }

    [Fact]
    public void EnvironmentTurnedOff_CanCompile()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeEnvironment", bool.FalseString);
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
    }

    [Fact]
    public void EnvironmentTurnedOff_CanNotUseIBitwardenEnvironment()
    {
        // Features and WebEssentials both transitively depend on Bitwarden.Server.Sdk.Environment,
        // so all three must be disabled to truly remove the type from the compilation.
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeEnvironment", bool.FalseString);
        project.WithProperty("BitIncludeFeatures", bool.FalseString);
        project.WithProperty("BitIncludeWebEssentials", bool.FalseString);
        project.WithDefaultProgramCs("""
            app.MapGet("/test", (Bitwarden.Server.Sdk.Environment.IBitwardenEnvironment env) => env.Version);
            """);

        var result = project.Build();

        Assert.False(result.Succeeded, result.GetConsoleLog());

        // error CS0234: The type or namespace name 'Environment' does not exist in the namespace 'Bitwarden.Server.Sdk' (are you missing an assembly reference?)
        Assert.Contains(result.ErrorEvents, e => e.Code == "CS0234");
    }

    [Theory]
    [InlineData("BitIncludeFeatures")]
    [InlineData("BitIncludeWebEssentials")]
    public void EnvironmentTurnedOff_WithDependentPackageOn_WarnsAboutIncompatibility(string dependentPackage)
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeEnvironment", bool.FalseString);
        project.WithProperty(dependentPackage, bool.TrueString);
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
        var warning = Assert.Single(result.WarningEvents, w => w.Code == "BW0004");
        Assert.Contains("BitIncludeEnvironment", warning.Message);
    }

    [Fact]
    public void EnvironmentTurnedOn_CanUseIBitwardenEnvironment()
    {
        using var project = new TempDotNetProject();
        project.WithProperty("BitIncludeEnvironment", bool.TrueString);
        project.WithDefaultProgramCs("""
            app.MapGet("/test", (Bitwarden.Server.Sdk.Environment.IBitwardenEnvironment env) => env.Version);
            """);

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
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
            { "BitIncludeWebEssentials", ["true", "false"] },
            { "BitIncludeEnvironment", ["true", "false"] },
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
        static string Serialize(Dictionary<string, string> properties) =>
            string.Join(',', properties.Select(p => $"{p.Key}={p.Value}"));
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

        using var project = new TempDotNetProject();
        foreach (var property in properties)
            project.WithProperty(property.Key, property.Value);
        project.WithDefaultProgramCs();

        var result = project.Build();

        Assert.True(result.Succeeded, result.GetConsoleLog());
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
