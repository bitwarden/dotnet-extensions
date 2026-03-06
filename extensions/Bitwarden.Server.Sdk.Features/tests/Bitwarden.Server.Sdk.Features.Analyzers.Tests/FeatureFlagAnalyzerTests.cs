using Microsoft.CodeAnalysis;
using Bitwarden.Server.Sdk.Features;
using Bitwarden.Server.Sdk.Features.Analyzers;

namespace Bitwarden.Server.Sdk.Analyzers.Tests;

public class FeatureFlagAnalyzerTests : AnalyzerTests<FeatureFlagAnalyzer>
{
    public FeatureFlagAnalyzerTests()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FlagKeyCollectionAttribute).Assembly.Location));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("\"   \"")]
    public async Task ShouldWarnAboutInvalidFlagKeyValue(string? flagValue)
    {
        await RunAnalyzerAsync(
            $$"""
            using Bitwarden.Server.Sdk.Features;

            [FlagKeyCollection]
            public class MyFlags
            {
                public const string {|BW0002:Flag|} = {{flagValue}};
            }
            """
        );
    }

    [Fact]
    public async Task ShouldSuggestRemovingValidFlagKeys()
    {
        await RunAnalyzerAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            [FlagKeyCollection]
            public class MyFlags
            {
                public const string {|BW0001:Flag|} = "my-flag";
                public const string {|BW0001:AnotherFlag|} = "another-flag";
            }
            """
        );
    }
}
