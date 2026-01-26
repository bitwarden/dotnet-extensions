using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Bitwarden.Server.Sdk.CodeFixers.Tests;

public class RemoveFeatureFlagCodeFixerTests : TestBase
{
    [Fact]
    public async Task ReplacesFeatureCheckWithTrueLiteral()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    var isEnabled = featureService.IsEnabled(MyFlags.Flag);
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
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
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    if (Get() && featureService.IsEnabled(MyFlags.Flag))
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

            namespace Test;

            public class Something
            {
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

    [Fact]
    public async Task ShouldRemoveIfCheck()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    if (featureService.IsEnabled(MyFlags.Flag))
                    {
                        Do(true);
                    }
                    else
                    {
                        Do(false);
                    }
                }

                private void Do(bool value) { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    Do(true);
                }

                private void Do(bool value) { }
            }
            """
        );
    }

    [Fact]
    public async Task ShouldRemoveIfBlockWhenNegated()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    if (!featureService.IsEnabled(MyFlags.Flag))
                    {
                        Do(true);
                    }
                    else
                    {
                        Do(false);
                    }
                }

                private void Do(bool value) { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    Do(false);
                }

                private void Do(bool value) { }
            }
            """
        );
    }

    [Fact]
    public async Task ShouldRemoveAllIfBlockWhenNegatedAndNoElseBlock()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    if (!featureService.IsEnabled(MyFlags.Flag))
                    {
                        Do(true);
                    }
                    Do(false);
                }

                private void Do(bool value) { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    Do(false);
                }

                private void Do(bool value) { }
            }
            """
        );
    }

    // TODO:
    [Fact]
    public async Task ShouldRemoveAllFlags()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    var isEnabled1 = featureService.IsEnabled(MyFlags.Flag);
                    var isEnabled2 = featureService.IsEnabled(MyFlags.AnotherFlag);
                    var isEnabled3 = featureService.IsEnabled(MyFlags.Flag);
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class Something
            {
                public Something(IFeatureService featureService)
                {
                    var isEnabled1 = true;
                    var isEnabled2 = featureService.IsEnabled(MyFlags.AnotherFlag);
                    var isEnabled3 = true;
                }
            }
            """
        );
    }

    [Fact]
    public async Task ControllerAttributeGetsRemoved()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IActionResult).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(ControllerBase).Assembly.Location));

        await RunCodeFixAsync(
            """
            using Microsoft.AspNetCore.Mvc;
            using Bitwarden.Server.Sdk.Features;

            public class MyController : ControllerBase
            {
                private const string Flag = "my-flag";

                [{|BW0002:RequireFeature(Flag)|}]
                public IActionResult MyAction()
                {
                    return NoContent();
                }
            }
            """,
            """
            using Microsoft.AspNetCore.Mvc;
            using Bitwarden.Server.Sdk.Features;

            public class MyController : ControllerBase
            {
                private const string Flag = "my-flag";

                public IActionResult MyAction()
                {
                    return NoContent();
                }
            }
            """
        );
    }

    [Fact]
    public async Task ControllerAttributeWithMultipleAttributesGetsRemoved()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IActionResult).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(ControllerBase).Assembly.Location));

        await RunCodeFixAsync(
            """
            using Microsoft.AspNetCore.Mvc;
            using Bitwarden.Server.Sdk.Features;

            public class MyController : ControllerBase
            {
                private const string Flag = "my-flag";

                [Route("/test"), {|BW0002:RequireFeature(Flag)|}]
                public IActionResult MyAction()
                {
                    return NoContent();
                }
            }
            """,
            """
            using Microsoft.AspNetCore.Mvc;
            using Bitwarden.Server.Sdk.Features;

            public class MyController : ControllerBase
            {
                private const string Flag = "my-flag";

                [Route("/test")]
                public IActionResult MyAction()
                {
                    return NoContent();
                }
            }
            """
        );
    }

    [Fact]
    public async Task MinimalApiEndpointMetadata_ChainedOnEndpoint()
    {
        await RunAspNetCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!")
                .{|BW0002:RequireFeature(Flags.Flag)|};

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!");

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """
        );
    }

    [Fact]
    public async Task MinimalApiEndpointMetadata_ChainedOnEndpoint_WithAnotherChainedCall()
    {
        await RunAspNetCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!")
                .{|BW0002:RequireFeature(Flags.Flag)|}
                .WithName("test");

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!")
                .WithName("test");

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """
        );
    }

    [Fact]
    public async Task MinimalApiEndpointMetadata_NotChained_ShouldRemoveWholeCall()
    {
        await RunAspNetCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            endpoint.{|BW0002:RequireFeature(Flags.Flag)|};

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """
        );
    }

    [Fact]
    public async Task MinimalApiEndpointMetadata_ChainedFromVariable_ShouldRemoveJustOurCall()
    {
        await RunAspNetCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            endpoint.{|BW0002:RequireFeature(Flags.Flag)|}.WithName("test");

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            endpoint.WithName("test");

            public static class Flags
            {
                public const string Flag = "my-flag";
            }
            """
        );
    }

    [Fact]
    public async Task TestCode_MocksTrue_DeletesSelf()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Substitute).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));

        await RunCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            public class TestClass
            {
                const string Flag = "my-flag";

                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod()
                {
                    {|BW0002:_featureService
                        .IsEnabled(Flag)|}
                        .Returns(true);
                }
            }
            """,
            """
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            public class TestClass
            {
                const string Flag = "my-flag";

                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod()
                {

                }
            }
            """
        );
    }

    [Fact]
    public async Task TestCode_MocksFalse_DeletesTest()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Substitute).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));

        await RunCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            public class TestClass
            {
                const string Flag = "my-flag";

                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod()
                {
                    {|BW0002:_featureService
                        .IsEnabled(Flag)|}
                        .Returns(false);
                }
            }
            """,
            """
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            public class TestClass
            {
                const string Flag = "my-flag";

                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }
            }
            """
        );
    }

    [Fact]
    public async Task TestCode_MocksNonConstant_AddsErrorDirective()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Substitute).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));

        await RunCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            public class TestClass
            {
                const string Flag = "my-flag";

                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Theory]
                [InlineData(true)]
                [InlineData(false)]
                public void TestMethod(bool flagValue)
                {
                    {|BW0002:_featureService
                        .IsEnabled(Flag)|}
                        .Returns(flagValue);
                }
            }
            """,
            """
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            public class TestClass
            {
                const string Flag = "my-flag";

                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Theory]
                [InlineData(true)]
                [InlineData(false)]
                public void TestMethod(bool flagValue)
                {

                }
            }
            """
        );
    }

    private async Task RunAspNetCodeFixAsync([StringSyntax("C#-test")] string inputSource, [StringSyntax("C#-test")] string expectedFixedSource)
    {
        TestState.OutputKind = OutputKind.ConsoleApplication;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(WebApplication).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(EndpointRouteBuilderExtensions).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IApplicationBuilder).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IHost).Assembly.Location));

        await RunCodeFixAsync(inputSource, expectedFixedSource);
    }

    private async Task RunDefaultCodeFixAsync([StringSyntax("C#-test")] string inputSource, [StringSyntax("C#-test")] string expectedFixedSource)
    {
        CodeActionEquivalenceKey = "BW0001-my-flag";
        TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
        CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;

        FixedState.ExpectedDiagnostics.Add(new DiagnosticResult("BW0001", DiagnosticSeverity.Info)
            .WithSpan("MyFlags.cs", 8, 25, 8, 36)
        );

        TestState.Sources.Add(("MyFlags.cs", /* lang=C#-test */ """
        using Bitwarden.Server.Sdk.Features;

        namespace Test;

        [FlagKeyCollection]
        public static partial class MyFlags
        {
            public const string {|BW0001:Flag|} = "my-flag";
            public const string {|BW0001:AnotherFlag|} = "another-flag";
        }
        """));
        FixedState.Sources.Add(("MyFlags.cs", /* lang=C#-test */ """
        using Bitwarden.Server.Sdk.Features;

        namespace Test;

        [FlagKeyCollection]
        public static partial class MyFlags
        {
            public const string {|BW0001:AnotherFlag|} = "another-flag";
        }
        """));
        await RunCodeFixAsync(inputSource, expectedFixedSource);
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
