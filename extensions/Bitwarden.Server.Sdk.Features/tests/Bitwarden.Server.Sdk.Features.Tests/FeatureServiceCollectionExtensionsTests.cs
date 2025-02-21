using Bitwarden.Server.Sdk.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.UnitTests.Features;

public class FeatureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddKnownFeatureFlags_Works()
    {
        var services = new ServiceCollection();
        services.AddKnownFeatureFlags(["feature-one", "feature-two"]);

        var sp = services.BuildServiceProvider();

        var featureFlagOptions = sp.GetRequiredService<IOptions<FeatureFlagOptions>>().Value;

        Assert.Equal(["feature-one", "feature-two"], featureFlagOptions.KnownFlags);
    }

    [Fact]
    public void AddKnownFeatureFlags_MultipleTimes_AddsAll()
    {
        var services = new ServiceCollection();
        services.AddKnownFeatureFlags(["feature-one", "feature-two"]);
        services.AddKnownFeatureFlags(["feature-three"]);

        var sp = services.BuildServiceProvider();

        var featureFlagOptions = sp.GetRequiredService<IOptions<FeatureFlagOptions>>().Value;

        Assert.Equal(["feature-one", "feature-two", "feature-three"], featureFlagOptions.KnownFlags);
    }

    [Fact]
    public void AddFeatureFlagValues_Works()
    {
        var services = new ServiceCollection();
        services.AddFeatureFlagValues(
            [
                KeyValuePair.Create("feature-one", "true"),
                KeyValuePair.Create("feature-two", "false"),
            ]
        );

        var sp = services.BuildServiceProvider();

        var featureFlagOptions = sp.GetRequiredService<IOptions<FeatureFlagOptions>>().Value;

        var featureOneValue = Assert.Contains("feature-one", featureFlagOptions.FlagValues);
        Assert.Equal("true", featureOneValue);

        var featureTwoValue = Assert.Contains("feature-two", featureFlagOptions.FlagValues);
        Assert.Equal("false", featureTwoValue);
    }

    [Fact]
    public void AddFeatureFlagValues_MultipleTimes_AddMoreAndOverwritesExisting()
    {
        var services = new ServiceCollection();
        services.AddFeatureFlagValues(
            [
                KeyValuePair.Create("feature-one", "true"),
                KeyValuePair.Create("feature-two", "false"),
            ]
        );

        services.AddFeatureFlagValues(
            [
                KeyValuePair.Create("feature-one", "value"), // Override existing value
                KeyValuePair.Create("feature-three", "1"),
            ]
        );

        var sp = services.BuildServiceProvider();

        var featureFlagOptions = sp.GetRequiredService<IOptions<FeatureFlagOptions>>().Value;

        var featureOneValue = Assert.Contains("feature-one", featureFlagOptions.FlagValues);
        Assert.Equal("value", featureOneValue);

        var featureTwoValue = Assert.Contains("feature-two", featureFlagOptions.FlagValues);
        Assert.Equal("false", featureTwoValue);

        var featureThreeValue = Assert.Contains("feature-three", featureFlagOptions.FlagValues);
        Assert.Equal("1", featureThreeValue);
    }
}
