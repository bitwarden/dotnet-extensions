using Microsoft.CodeAnalysis;
using Bitwarden.Server.Sdk.Features;

namespace Bitwarden.Server.Sdk.Analyzers.Tests;

public class FeatureFlagAnalyzerTests : AnalyzerTests<FeatureFlagAnalyzer>
{
    public FeatureFlagAnalyzerTests()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IFeatureService).Assembly.Location));
    }

    [Fact]
    public async Task ShouldWarnIfConstNotUsed()
    {
        await RunAnalyzerAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    var enabled = featureService.IsEnabled({|BW0001:"my-flag"|});
                }
            }
            """
        );
    }

    [Fact]
    public async Task ShouldSuggestRemovingFeature()
    {
        await RunAnalyzerAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                public const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    var isEnabled = {|BW0002:featureService.IsEnabled(Flag)|};
                }
            }
            """
        );
    }
}
