using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Features;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.Analyzers;

public class RemoveFeatureFlagCodeFixerTests : CSharpCodeFixTest<FeatureFlagAnalyzer, RemoveFeatureFlagCodeFixer, DefaultVerifier>
{
    [Fact]
    public async Task Works()
    {
        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    var isEnabled = {|BW0002:featureService.IsEnabled(Flag)|};
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    var isEnabled = true;
                }
            }
            """
        );
    }

    [Fact]
    public async Task ShouldSimplifyBinaryExpression()
    {
        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    if (Get() && {|BW0002:featureService.IsEnabled(Flag)|})
                    {
                        Do();
                    }
                }

                private bool Get() => true;
                private void Do() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    if (Get())
                    {
                        Do();
                    }
                }

                private bool Get() => true;
                private void Do() { }
            }
            """
        );
    }


    private async Task RunCodeFixAsync([StringSyntax("C#-test")] string inputSource, [StringSyntax("C#-test")] string expectedFixedSource)
    {
        TestCode = inputSource;
        FixedCode = expectedFixedSource;

        TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IFeatureService).Assembly.Location));

        await RunAsync(TestContext.Current.CancellationToken);
    }
}
