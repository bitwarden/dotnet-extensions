using System.Diagnostics.CodeAnalysis;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Analysis;

public partial class RestrictedDependencyUseClassifierTests
{
    private const string ClassicConstructor = """
        public class Consumer
        {
            private readonly IUserService _userService;

            public Consumer(IUserService {|BW0005:userService|})
            {
                _userService = userService;
            }
        }
        """;

    private const string PrimaryConstructor = """
        public class Consumer(IUserService {|BW0005:userService|})
        {
            private readonly IUserService _userService = userService;
        }
        """;

    private const string ConstrainedPrimaryConstructor = """
        public class Consumer<T>(T {|BW0005:userService|}) where T : IUserService
        {
            private readonly T _userService = userService;
        }
        """;

    [Theory]
    [InlineData(ClassicConstructor)]
    [InlineData(PrimaryConstructor)]
    [InlineData(ConstrainedPrimaryConstructor)]
    public async Task ConstructorParameter_NotBaselined_ReportsInjection([StringSyntax("C#-test")] string consumer)
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer($$"""
                using Test;

                namespace Test;

                {{consumer}}
                """)
            .RunAsync();
    }

    private const string HelperInjectingTheRestrictedType = """
        using Test;

        namespace Test;

        public class Helper
        {
            public Helper(IUserService userService)
            {
            }
        }
        """;

    /// <summary>
    /// AllowedPaths globs are repo-relative and forward-slashed, so the separator style the
    /// compiler happens to hand us must not change whether a file is exempt.
    /// </summary>
    [Theory]
    [InlineData(SampleProjectFixture.RepoRoot, SampleProjectFixture.ServicePath, "/repo/src/Core/Services/Helper.cs")]
    [InlineData(@"C:\repo\", @"C:\repo\src\Core\Services\IUserService.cs", @"C:\repo\src\Core\Services\Helper.cs")]
    public async Task ConstructorParameter_UnderAllowedPath_ReportsNothing(string repoRoot, string servicePath, string helperPath)
    {
        await new AnalyzerHarness(repoRoot: repoRoot)
            .WithSource(servicePath, SampleProjectFixture.Service)
            .WithBaselineJson(SampleProjectFixture.Baseline())
            .WithSource(helperPath, HelperInjectingTheRestrictedType)
            .RunAsync();
    }
}
