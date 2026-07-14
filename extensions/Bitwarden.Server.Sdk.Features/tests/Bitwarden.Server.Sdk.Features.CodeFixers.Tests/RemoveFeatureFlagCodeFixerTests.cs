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

    [Fact]
    public async Task BracelessIfCheck_InlinesBody()
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
                        Do(true);
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
    public async Task BracelessNegatedIfCheck_NoElse_RemovesIf()
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
                        Do(true);
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

    [Fact]
    public async Task BracelessIfCheck_WithBracelessElse_InlinesThenBody()
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
                        Do(true);
                    else
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
                    Do(true);
                }

                private void Do(bool value) { }
            }
            """
        );
    }

    [Fact]
    public async Task BracelessNegatedIfCheck_WithBracelessElse_InlinesElseBody()
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
                        Do(true);
                    else
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

        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Mvc;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyController : ControllerBase
            {
                [RequireFeature(MyFlags.Flag)]
                public IActionResult MyAction()
                {
                    return NoContent();
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using Microsoft.AspNetCore.Mvc;

            namespace Test;

            public class MyController : ControllerBase
            {
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

        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Mvc;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyController : ControllerBase
            {
                [Route("/test"), RequireFeature(MyFlags.Flag)]
                public IActionResult MyAction()
                {
                    return NoContent();
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using Microsoft.AspNetCore.Mvc;

            namespace Test;

            public class MyController : ControllerBase
            {
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
        ConfigureForAspNet();
        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!")
                .RequireFeature(MyFlags.Flag);
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!");
            """
        );
    }

    [Fact]
    public async Task MinimalApiEndpointMetadata_ChainedOnEndpoint_WithAnotherChainedCall()
    {
        ConfigureForAspNet();
        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!")
                .RequireFeature(MyFlags.Flag)
                .WithName("test");
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            app.MapGet("/", () => "Hello world!")
                .WithName("test");
            """
        );
    }

    [Fact]
    public async Task MinimalApiEndpointMetadata_NotChained_ShouldRemoveWholeCall()
    {
        ConfigureForAspNet();
        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            endpoint.RequireFeature(MyFlags.Flag);

            app.Run();
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");

            app.Run();
            """
        );
    }

    [Fact]
    public async Task MinimalApiEndpointMetadata_ChainedFromVariable_ShouldRemoveJustOurCall()
    {
        ConfigureForAspNet();
        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            endpoint.RequireFeature(MyFlags.Flag).WithName("test");
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            endpoint.WithName("test");
            """
        );
    }

    [Fact]
    public async Task RequireFeatureMethod_OnlyStatementInBlock_LeavesEmptyBlock()
    {
        ConfigureForAspNet();
        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            Configure(endpoint);

            app.Run();

            static void Configure(IEndpointConventionBuilder endpoint)
            {
                endpoint.RequireFeature(MyFlags.Flag);
            }
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            Configure(endpoint);

            app.Run();

            static void Configure(IEndpointConventionBuilder endpoint)
            {
            }
            """
        );
    }

    [Fact]
    public async Task RequireFeatureMethod_FirstStatementInBlock_RemovesAndPreservesIndentation()
    {
        ConfigureForAspNet();
        await RunDefaultCodeFixAsync(
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            Configure(endpoint);

            app.Run();

            static void Configure(IEndpointConventionBuilder endpoint)
            {
                endpoint.RequireFeature(MyFlags.Flag);
                endpoint.WithName("my-endpoint");
            }
            """,
            """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Test;

            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            var endpoint = app.MapGet("/", () => "Hello world!");
            Configure(endpoint);

            app.Run();

            static void Configure(IEndpointConventionBuilder endpoint)
            {
                endpoint.WithName("my-endpoint");
            }
            """
        );
    }

    [Fact]
    public async Task TestCode_MocksTrue_DeletesSelf()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Substitute).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));

        await RunDefaultCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class TestClass
            {
                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod()
                {
                    _featureService
                        .IsEnabled(MyFlags.Flag)
                        .Returns(true);
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using NSubstitute;
            using Xunit;

            namespace Test;

            public class TestClass
            {
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

        await RunDefaultCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class TestClass
            {
                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod()
                {
                    _featureService
                        .IsEnabled(MyFlags.Flag)
                        .Returns(false);
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using NSubstitute;
            using Xunit;

            namespace Test;

            public class TestClass
            {
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
    public async Task TestCode_MultipleMocksFalse_DeletesAllTests()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Substitute).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));

        await RunDefaultCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class TestClass
            {
                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod1()
                {
                    _featureService
                        .IsEnabled(MyFlags.Flag)
                        .Returns(false);
                }

                [Fact]
                public void TestMethod2()
                {
                    _featureService
                        .IsEnabled(MyFlags.Flag)
                        .Returns(false);
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using NSubstitute;
            using Xunit;

            namespace Test;

            public class TestClass
            {
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
    public async Task TestCode_MultipleMocksTrue_DeletesAllExpressions()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Substitute).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));

        await RunDefaultCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class TestClass
            {
                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod1()
                {
                    _featureService
                        .IsEnabled(MyFlags.Flag)
                        .Returns(true);
                    Do();
                }

                [Fact]
                public void TestMethod2()
                {
                    _featureService
                        .IsEnabled(MyFlags.Flag)
                        .Returns(true);
                    Do();
                }

                private void Do() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using NSubstitute;
            using Xunit;

            namespace Test;

            public class TestClass
            {
                private readonly IFeatureService _featureService;

                public TestClass()
                {
                    _featureService = Substitute.For<IFeatureService>();
                }

                [Fact]
                public void TestMethod1()
                {

                    Do();
                }

                [Fact]
                public void TestMethod2()
                {

                    Do();
                }

                private void Do() { }
            }
            """
        );
    }

    [Fact]
    public async Task TestCode_MocksNonConstant_AddsErrorDirective()
    {
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(Substitute).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));

        await RunDefaultCodeFixAsync("""
            using NSubstitute;
            using Xunit;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class TestClass
            {
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
                    _featureService
                        .IsEnabled(MyFlags.Flag)
                        .Returns(flagValue);
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using NSubstitute;
            using Xunit;

            namespace Test;

            public class TestClass
            {
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

    [Fact]
    public async Task MultipleFeatureFlags_RemovesOnlySpecifiedFlag()
    {
        // This test uses RunCodeFixAsync to verify only the specified flag is removed
        // while other flags remain unchanged
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

        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                public MyService(IFeatureService featureService)
                {
                    if (featureService.IsEnabled(MyFlags.Flag))
                    {
                        Setup1();
                    }
                    if (featureService.IsEnabled(MyFlags.AnotherFlag))
                    {
                        Setup2();
                    }
                }

                private void Setup1() { }
                private void Setup2() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                public MyService(IFeatureService featureService)
                {
                    Setup1();
                    if (featureService.IsEnabled(MyFlags.AnotherFlag))
                    {
                        Setup2();
                    }
                }

                private void Setup1() { }
                private void Setup2() { }
            }
            """
        );
    }

    [Fact]
    public async Task TernaryOperator_SimplifiestoTrueBranch()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public string GetValue()
                {
                    var result = _featureService.IsEnabled(MyFlags.Flag)
                        ? "new-value"
                        : "old-value";
                    return result;
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public string GetValue()
                {
                    var result = "new-value";
                    return result;
                }
            }
            """
        );
    }

    [Fact]
    public async Task TernaryOperator_InlineExpression()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public string GetValue()
                {
                    return _featureService.IsEnabled(MyFlags.Flag) ? "new-value" : "old-value";
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public string GetValue()
                {
                    return "new-value";
                }
            }
            """
        );
    }

    [Fact]
    public async Task SwitchExpression_SimplifiesToTrueBranch()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public enum PlanType { Free, Premium }

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public string GetPlanName(PlanType plan)
                {
                    return plan switch
                    {
                        PlanType.Free => "free",
                        PlanType.Premium => _featureService.IsEnabled(MyFlags.Flag)
                            ? "premium-v2"
                            : "premium",
                        _ => "unknown"
                    };
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public enum PlanType { Free, Premium }

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public string GetPlanName(PlanType plan)
                {
                    return plan switch
                    {
                        PlanType.Free => "free",
                        PlanType.Premium => "premium-v2",
                        _ => "unknown"
                    };
                }
            }
            """
        );
    }

    [Fact]
    public async Task NegationPattern_SimplifiesToFalse()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public void Execute()
                {
                    var shouldLogout = !_featureService.IsEnabled(MyFlags.Flag);
                    if (shouldLogout)
                    {
                        Logout();
                    }
                }

                private void Logout() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public void Execute()
                {
                    var shouldLogout = false;
                    if (shouldLogout)
                    {
                        Logout();
                    }
                }

                private void Logout() { }
            }
            """
        );
    }

    [Fact]
    public async Task VariableAssignment_SimplifiesToTrue()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public void Execute()
                {
                    var useNewFeature = _featureService.IsEnabled(MyFlags.Flag);

                    if (useNewFeature)
                    {
                        DoNewThing();
                    }
                    else
                    {
                        DoOldThing();
                    }
                }

                private void DoNewThing() { }
                private void DoOldThing() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public void Execute()
                {
                    var useNewFeature = true;

                    if (useNewFeature)
                    {
                        DoNewThing();
                    }
                    else
                    {
                        DoOldThing();
                    }
                }

                private void DoNewThing() { }
                private void DoOldThing() { }
            }
            """
        );
    }

    [Fact]
    public async Task MethodParameter_SimplifiesToTrue()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyEntity
            {
                public string Name { get; set; }

                public void UpdateFromLicense(License license, IFeatureService featureService)
                {
                    Name = license.Name;

                    if (featureService.IsEnabled(MyFlags.Flag))
                    {
                        ApplyNewBehavior();
                    }
                }

                private void ApplyNewBehavior() { }
            }

            public class License
            {
                public string Name { get; set; }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyEntity
            {
                public string Name { get; set; }

                public void UpdateFromLicense(License license, IFeatureService featureService)
                {
                    Name = license.Name;

                    ApplyNewBehavior();
                }

                private void ApplyNewBehavior() { }
            }

            public class License
            {
                public string Name { get; set; }
            }
            """
        );
    }

    [Fact]
    public async Task FeatureRoutedService_SimplifiesToNewImplementation()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;
                private readonly INewService _newService;
                private readonly IOldService _oldService;

                public MyService(IFeatureService featureService, INewService newService, IOldService oldService)
                {
                    _featureService = featureService;
                    _newService = newService;
                    _oldService = oldService;
                }

                public string GetData()
                {
                    if (_featureService.IsEnabled(MyFlags.Flag))
                    {
                        return _newService.GetData();
                    }

                    return _oldService.GetData();
                }
            }

            public interface INewService
            {
                string GetData();
            }

            public interface IOldService
            {
                string GetData();
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;
                private readonly INewService _newService;
                private readonly IOldService _oldService;

                public MyService(IFeatureService featureService, INewService newService, IOldService oldService)
                {
                    _featureService = featureService;
                    _newService = newService;
                    _oldService = oldService;
                }

                public string GetData()
                {
                    return _newService.GetData();
                }
            }

            public interface INewService
            {
                string GetData();
            }

            public interface IOldService
            {
                string GetData();
            }
            """
        );
    }

    [Fact]
    public async Task ExpressionBodyWithAndAlsoAccessCheck_KeepsAccessCheck()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public interface IAccessService { bool HasPermission(); }

            public class MyService
            {
                private readonly IFeatureService _featureService;
                private readonly IAccessService _accessService;

                public MyService(IFeatureService featureService, IAccessService accessService)
                {
                    _featureService = featureService;
                    _accessService = accessService;
                }

                private bool CanDoThing() =>
                    _featureService.IsEnabled(MyFlags.Flag)
                    && _accessService.HasPermission();
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public interface IAccessService { bool HasPermission(); }

            public class MyService
            {
                private readonly IFeatureService _featureService;
                private readonly IAccessService _accessService;

                public MyService(IFeatureService featureService, IAccessService accessService)
                {
                    _featureService = featureService;
                    _accessService = accessService;
                }

                private bool CanDoThing() =>
                    _accessService.HasPermission();
            }
            """
        );
    }

    [Fact]
    public async Task NestedIfsInIfBlock_InlinesBodyStatements()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                public string? GetValue(IFeatureService featureService, bool conditionA, bool conditionB, bool conditionC)
                {
                    if (featureService.IsEnabled(MyFlags.Flag))
                    {
                        if (conditionA)
                        {
                            return null;
                        }

                        if (conditionB)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        if (conditionC)
                        {
                            return null;
                        }
                    }

                    return "value";
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                public string? GetValue(IFeatureService featureService, bool conditionA, bool conditionB, bool conditionC)
                {
                    if (conditionA)
                    {
                        return null;
                    }
                    if (conditionB)
                    {
                        return null;
                    }

                    return "value";
                }
            }
            """
        );
    }

    [Fact]
    public async Task LocalFunctionsAfterReturn_ArePreserved()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public bool IsEnabled() =>
                    _featureService.IsEnabled(MyFlags.Flag);

                public void Process(bool condition)
                {
                    if (condition)
                    {
                        Helper();
                    }

                    return;

                    void Helper() { }
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly IFeatureService _featureService;

                public MyService(IFeatureService featureService)
                {
                    _featureService = featureService;
                }

                public bool IsEnabled() => true;

                public void Process(bool condition)
                {
                    if (condition)
                    {
                        Helper();
                    }

                    return;

                    void Helper() { }
                }
            }
            """
        );
    }

    [Fact]
    public async Task NegatedFlagInOrChain_FoldsFalseOperandOut()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly bool _cond1;
                private readonly bool _cond2;

                public MyService(bool cond1, bool cond2)
                {
                    _cond1 = cond1;
                    _cond2 = cond2;
                }

                public void Process(IFeatureService featureService)
                {
                    if (!featureService.IsEnabled(MyFlags.Flag)
                        || _cond1
                        || _cond2)
                    {
                        return;
                    }

                    DoWork();
                }

                private void DoWork() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly bool _cond1;
                private readonly bool _cond2;

                public MyService(bool cond1, bool cond2)
                {
                    _cond1 = cond1;
                    _cond2 = cond2;
                }

                public void Process(IFeatureService featureService)
                {
                    if (_cond1
                        || _cond2)
                    {
                        return;
                    }

                    DoWork();
                }

                private void DoWork() { }
            }
            """
        );
    }

    [Fact]
    public async Task NegatedFlagInAndChain_FalseShortCircuits_RemovesBlock()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly bool _condition;

                public MyService(bool condition)
                {
                    _condition = condition;
                }

                public void Process(IFeatureService featureService)
                {
                    if (!featureService.IsEnabled(MyFlags.Flag) && _condition)
                    {
                        DoThing();
                    }

                    Continue();
                }

                private void DoThing() { }
                private void Continue() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private readonly bool _condition;

                public MyService(bool condition)
                {
                    _condition = condition;
                }

                public void Process(IFeatureService featureService)
                {

                    Continue();
                }

                private void DoThing() { }
                private void Continue() { }
            }
            """
        );
    }

    [Fact]
    public async Task OrBinary_MultiLine_RightSideRemoved_CleanCondition()
    {
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private bool _condition;

                public MyService(bool condition)
                {
                    _condition = condition;
                }

                public void DoThing(IFeatureService featureService)
                {
                    if (_condition ||
                        featureService.IsEnabled(MyFlags.Flag))
                    {
                        Work();
                    }
                }

                private void Work() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                private bool _condition;

                public MyService(bool condition)
                {
                    _condition = condition;
                }

                public void DoThing(IFeatureService featureService)
                {
                    if (_condition)
                    {
                        Work();
                    }
                }

                private void Work() { }
            }
            """
        );
    }

    [Fact]
    public async Task CommentedRequireFeature_RemainsUnchanged()
    {
        // This test documents that commented attributes are NOT automatically removed
        // This is by design - developers may have intentionally commented them for a reason
        await RunDefaultCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                // [RequireFeature(MyFlags.Flag)] /* Uncomment once client fallback re-try logic is added */
                public void DoSomething()
                {
                    ProcessData();
                }

                private void ProcessData() { }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyService
            {
                // [RequireFeature(MyFlags.Flag)] /* Uncomment once client fallback re-try logic is added */
                public void DoSomething()
                {
                    ProcessData();
                }

                private void ProcessData() { }
            }
            """
        );
    }

    private void ConfigureForAspNet()
    {
        TestState.OutputKind = OutputKind.ConsoleApplication;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(WebApplication).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(EndpointRouteBuilderExtensions).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IApplicationBuilder).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IHost).Assembly.Location));
    }

    [Fact]
    public async Task FlagDefinedInSeparateProject_FixesReferencesInConsumingProject()
    {
        CodeActionEquivalenceKey = "BW0001-my-flag";
        TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
        CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;

        var flagsProject = new ProjectState("FlagsProject", LanguageNames.CSharp, "FlagsProject/", ".cs");
        flagsProject.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        flagsProject.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(IFeatureService).Assembly.Location));
        flagsProject.Sources.Add(("MyFlags.cs", """
            using Bitwarden.Server.Sdk.Features;

            namespace Flags;

            [FlagKeyCollection]
            public static partial class MyFlags
            {
                public const string Flag = "my-flag";
            }
            """));
        flagsProject.Sources.Add(("FlagsService.cs", """
            using Bitwarden.Server.Sdk.Features;

            namespace Flags;

            public class FlagsService
            {
                public FlagsService(IFeatureService featureService)
                {
                    var isEnabled = featureService.IsEnabled(MyFlags.Flag);
                }
            }
            """));

        TestState.AdditionalProjects.Add("FlagsProject", flagsProject);
        TestState.AdditionalProjectReferences.Add("FlagsProject");
        TestState.ExpectedDiagnostics.Add(
            new DiagnosticResult("BW0001", DiagnosticSeverity.Info)
                .WithSpan("MyFlags.cs", 8, 25, 8, 29)
                .WithArguments("my-flag"));

        var fixedFlagsProject = new ProjectState("FlagsProject", LanguageNames.CSharp, "FlagsProject/", ".cs");
        fixedFlagsProject.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        fixedFlagsProject.AdditionalReferences.Add(
            MetadataReference.CreateFromFile(typeof(IFeatureService).Assembly.Location));
        fixedFlagsProject.Sources.Add(("MyFlags.cs", """
            using Bitwarden.Server.Sdk.Features;

            namespace Flags;

            [FlagKeyCollection]
            public static partial class MyFlags
            {
            }
            """));
        fixedFlagsProject.Sources.Add(("FlagsService.cs", """
            using Bitwarden.Server.Sdk.Features;

            namespace Flags;

            public class FlagsService
            {
                public FlagsService(IFeatureService featureService)
                {
                    var isEnabled = true;
                }
            }
            """));

        FixedState.AdditionalProjects.Add("FlagsProject", fixedFlagsProject);
        FixedState.AdditionalProjectReferences.Add("FlagsProject");

        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;
            using Flags;

            namespace Test;

            public class MyService
            {
                public MyService(IFeatureService featureService)
                {
                    var isEnabled = featureService.IsEnabled(MyFlags.Flag);
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using Flags;

            namespace Test;

            public class MyService
            {
                public MyService(IFeatureService featureService)
                {
                    var isEnabled = true;
                }
            }
            """
        );
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
