using Microsoft.CodeAnalysis.Testing;

namespace Bitwarden.Server.Sdk.RestrictedDependencies.Analyzers.Tests.Diagnostics;

/// <summary>
/// Roslyn honors <c>[SuppressMessage]</c> for analyzer errors, so it silences the underlying
/// BW0005; a <c>#pragma</c> does not, because the family is not configurable. Both are reported as
/// BW0012, without a source location precisely so that the same directive cannot silence it too.
/// </summary>
public class DiagnosticSuppressionScannerTests
{
    [Fact]
    public async Task PragmaDisable_OfRatchetId_ReportsUnstructuredSuppressionAndDoesNotSilence()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using Test;

                namespace Test;

                public class Consumer
                {
                    #pragma warning disable BW0005
                    public Consumer(IUserService {|BW0005:userService|})
                    {
                    }
                    #pragma warning restore BW0005
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments("#pragma warning disable at 'src/Api/Consumer.cs' line 7", "BW0005"))
            .RunAsync();
    }

    [Fact]
    public async Task PragmaDisable_OfItself_IsStillReported()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                #pragma warning disable BW0012
                namespace Test;

                public class Consumer
                {
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments("#pragma warning disable at 'src/Api/Consumer.cs' line 1", "BW0012"))
            .RunAsync();
    }

    [Fact]
    public async Task SuppressMessageAttribute_OfRatchetId_ReportsUnstructuredSuppression()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System.Diagnostics.CodeAnalysis;
                using Test;

                namespace Test;

                public class Consumer
                {
                    [SuppressMessage("RestrictedDependencies", "BW0005:Restricted dependency injected at a new site", Justification = "legacy")]
                    public Consumer(IUserService userService)
                    {
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments("[SuppressMessage] on '.ctor' at 'src/Api/Consumer.cs' line 8", "BW0005:Restricted dependency injected at a new site"))
            .RunAsync();
    }

    [Fact]
    public async Task SuppressMessageAttribute_OnAnAccessor_ReportsUnstructuredSuppression()
    {
        await AnalyzerHarness.WithBaseline(SampleProjectFixture.Site(DependencyUsageType.Injection, SampleProjectFixture.ConstructorSite))
            .WithConsumer("""
                using System.Diagnostics.CodeAnalysis;
                using System.Threading.Tasks;
                using Test;

                namespace Test;

                public class Consumer
                {
                    private readonly IUserService _userService;

                    public Consumer(IUserService userService)
                    {
                        _userService = userService;
                    }

                    public Task<bool> Premium
                    {
                        [SuppressMessage("RestrictedDependencies", "BW0006")]
                        get { return _userService.CanAccessPremium(new User()); }
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments("[SuppressMessage] on 'get_Premium' at 'src/Api/Consumer.cs' line 18", "BW0006"))
            .RunAsync();
    }

    [Fact]
    public async Task SuppressMessageAttribute_OnAStaticConstructor_ReportsUnstructuredSuppression()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System.Diagnostics.CodeAnalysis;

                namespace Test;

                public class Consumer
                {
                    [SuppressMessage("RestrictedDependencies", "BW0005")]
                    static Consumer()
                    {
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments("[SuppressMessage] on '.cctor' at 'src/Api/Consumer.cs' line 7", "BW0005"))
            .RunAsync();
    }

    [Fact]
    public async Task SuppressMessageAttribute_OnALocalFunction_ReportsUnstructuredSuppression()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System.Diagnostics.CodeAnalysis;

                namespace Test;

                public class Consumer
                {
                    public void Run()
                    {
                        [SuppressMessage("RestrictedDependencies", "BW0005")]
                        static void Helper()
                        {
                        }

                        Helper();
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments("[SuppressMessage] on 'Helper' at 'src/Api/Consumer.cs' line 9", "BW0005"))
            .RunAsync();
    }

    [Theory]
    [InlineData("assembly", "", "TestProject")]
    [InlineData("module", "", "TestProject.dll")]
    [InlineData("assembly", ", Scope = \"member\", Target = \"M:Test.Consumer.#ctor(Test.IUserService)\"", "TestProject")]
    [InlineData("module", ", Scope = \"member\", Target = \"M:Test.Consumer.#ctor(Test.IUserService)\"", "TestProject.dll")]
    public async Task GlobalSuppressMessage_OfRatchetId_ReportsUnstructuredSuppression(string target, string scope, string symbolName)
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer($$"""
                using System.Diagnostics.CodeAnalysis;
                using Test;

                [{{target}}: SuppressMessage("RestrictedDependencies", "BW0005"{{scope}})]

                namespace Test;

                public class Consumer
                {
                    public Consumer(IUserService userService)
                    {
                    }
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments($"[SuppressMessage] on '{symbolName}' at 'src/Api/Consumer.cs' line 4", "BW0005"))
            .RunAsync();
    }

    [Fact]
    public async Task PragmaDisable_OfOtherIds_ReportsNothing()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                namespace Test;

                public class Consumer
                {
                    #pragma warning disable CS0618
                    public void Run()
                    {
                    }
                    #pragma warning restore CS0618
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task SuppressMessageAttribute_OfOtherIds_ReportsNothing()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                using System.Diagnostics.CodeAnalysis;

                namespace Test;

                public class Consumer
                {
                    [SuppressMessage("Usage", "CA1801:Review unused parameters", Justification = "interface contract")]
                    public void Run(int unused)
                    {
                    }
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task PragmaDisable_WithNoIds_ReportsItAsSilencingTheExpiryWarning()
    {
        await AnalyzerHarness.WithBaseline()
            .WithConsumer("""
                #pragma warning disable
                namespace Test;

                public class Consumer
                {
                }
                """)
            .Expect(new DiagnosticResult(DiagnosticDescriptors.UnstructuredSuppression)
                .WithArguments("#pragma warning disable at 'src/Api/Consumer.cs' line 1", $"every warning, including {DiagnosticDescriptors.ExceptionExpired.Id}"))
            .RunAsync();
    }

    [Fact]
    public async Task PragmaDisable_WithNoIds_InGeneratedCode_ReportsNothing()
    {
        // Every source generator opens its output this way; reporting it would fail any project
        // that uses one.
        await AnalyzerHarness.WithBaseline()
            .WithSource("/repo/src/Api/obj/Debug/Generated/Consumer.g.cs", """
                // <auto-generated/>
                #pragma warning disable
                namespace Test;

                public class Consumer
                {
                }
                """)
            .RunAsync();
    }

    [Fact]
    public async Task PragmaDisable_WithNoIds_InGeneratedCode_DoesNotSilenceTheGate()
    {
        await AnalyzerHarness.WithBaseline()
            .WithSource("/repo/src/Api/obj/Debug/Generated/Consumer.g.cs", """
                // <auto-generated/>
                #pragma warning disable
                using Test;

                namespace Test;

                public class Consumer
                {
                    public Consumer(IUserService {|BW0005:userService|})
                    {
                    }
                }
                """)
            .RunAsync();
    }
}
