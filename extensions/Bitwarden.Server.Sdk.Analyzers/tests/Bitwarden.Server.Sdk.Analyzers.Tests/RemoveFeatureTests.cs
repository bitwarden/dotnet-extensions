using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Bitwarden.Server.Sdk.Features;

namespace Bitwarden.Server.Sdk.Analyzers.Tests;

public class RemoveFeatureTests : CSharpAnalyzerTest<FeatureFlagAnalyzer, DefaultVerifier>
{
    [Fact]
    public async Task ShouldWarnIfConstNotUsed()
    {
        await RunAnalayzerAsync(
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
        await RunAnalayzerAsync(
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

    private async Task RunAnalayzerAsync([StringSyntax("C#-test")] string source)
    {
        TestCode = source;
        TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IFeatureService).Assembly.Location));
        await RunAsync(TestContext.Current.CancellationToken);
    }
}
