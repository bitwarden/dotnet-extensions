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
    public async Task Trivia_OnMethod()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                /// <cleanup cref="MyFlags.Flag" />
                public void DoThing()
                {
                    // Legacy and should go away when a feature goes away
                }

                public void DoOtherThing()
                {

                }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void DoOtherThing()
                {

                }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnProperty()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyEntity
            {
                public decimal FirstProp { get; set; }
                /// <cleanup cref="MyFlags.Flag" />
                public int MyProp { get; set; }
                public string? AnotherProp { get; set; }
            }
            """,
            """
            namespace Test;

            public class MyEntity
            {
                public decimal FirstProp { get; set; }
                public string? AnotherProp { get; set; }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_InsideMethod()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                public void DoThing()
                {
                    /// <cleanup cref="MyFlags.Flag" />
                    Run();
                }

                public void Run()
                {

                }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void DoThing()
                {
                }

                public void Run()
                {

                }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnField()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyEntity
            {
                private int _firstField;
                /// <cleanup cref="MyFlags.Flag" />
                private string _featureField;
                private bool _lastField;
            }
            """,
            """
            namespace Test;

            public class MyEntity
            {
                private int _firstField;
                private bool _lastField;
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnConstructor()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                /// <cleanup cref="MyFlags.Flag" />
                public MyService(string name)
                {
                }

                public void DoWork() { }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void DoWork() { }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnNestedClass()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                /// <cleanup cref="MyFlags.Flag" />
                public class NestedFeatureClass
                {
                    public void Method() { }
                }

                public void OtherMethod() { }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void OtherMethod() { }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnMultipleConsecutiveStatements()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                public void DoThing()
                {
                    /// <cleanup cref="MyFlags.Flag" />
                    Run1();
                    /// <cleanup cref="MyFlags.Flag" />
                    Run2();
                    Run3();
                }

                public void Run1() { }
                public void Run2() { }
                public void Run3() { }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void DoThing()
                {
                    Run3();
                }

                public void Run1() { }
                public void Run2() { }
                public void Run3() { }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnInterfaceMember()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public interface IMyService
            {
                void RegularMethod();
                /// <cleanup cref="MyFlags.Flag" />
                void FeatureMethod();
            }
            """,
            """
            namespace Test;

            public interface IMyService
            {
                void RegularMethod();
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnEvent()
    {
        await RunDefaultCodeFixAsync(
            """
            using System;

            namespace Test;

            public class MyService
            {
                public event EventHandler? RegularEvent;
                /// <cleanup cref="MyFlags.Flag" />
                public event EventHandler? FeatureEvent;
            }
            """,
            """
            using System;

            namespace Test;

            public class MyService
            {
                public event EventHandler? RegularEvent;
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnIndexer()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyCollection
            {
                /// <cleanup cref="MyFlags.Flag" />
                public string this[int index] => "";

                public void OtherMethod() { }
            }
            """,
            """
            namespace Test;

            public class MyCollection
            {
                public void OtherMethod() { }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_EmptyClassAfterRemoval()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                /// <cleanup cref="MyFlags.Flag" />
                public void OnlyMethod() { }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnGenericMethod()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                /// <cleanup cref="MyFlags.Flag" />
                public T GetValue<T>() => default;

                public void OtherMethod() { }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void OtherMethod() { }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnDelegate()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                /// <cleanup cref="MyFlags.Flag" />
                public delegate void MyDelegate();

                public void OtherMethod() { }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void OtherMethod() { }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_WithRegularComments()
    {
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public class MyService
            {
                // Regular comment about the feature
                /// <cleanup cref="MyFlags.Flag" />
                public void FeatureMethod() { }

                public void OtherMethod() { }
            }
            """,
            """
            namespace Test;

            public class MyService
            {
                public void OtherMethod() { }
            }
            """
        );
    }

    [Fact]
    public async Task Trivia_OnRecordParameter()
    {
        // Note: Record parameters with feature comments remove the entire record
        // because the parameter is part of the record declaration syntax itself
        await RunDefaultCodeFixAsync(
            """
            namespace Test;

            public record MyRecord(
                int RegularProperty,
                /// <cleanup cref="MyFlags.Flag" />
                int FeatureProperty,
                string AnotherProperty
            );

            public class OtherClass
            {
                public void Method() { }
            }
            """,
            """
            namespace Test;

            public class OtherClass
            {
                public void Method() { }
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

    [Fact]
    public async Task RequiredCommentAddsRequiredAttributeAndImportsIt()
    {
        await RunDefaultCodeFixAsync(
            """
            using System;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyResponseModel
            {
                /// <required cref="MyFlags.Flag" />
                public string Name { get; set; }
                public Guid Something { get; set; }
            }
            """,
            """
            using System;
            using System.ComponentModel.DataAnnotations;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyResponseModel
            {
                [Required]
                public string Name { get; set; }
                public Guid Something { get; set; }
            }
            """
        );
    }

    [Fact]
    public async Task RequiredCommentAddsRequiredAttributeButDoesNotNeedToImport()
    {
        await RunDefaultCodeFixAsync(
            """
            using System;
            using System.ComponentModel.DataAnnotations;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyResponseModel
            {
                /// <required cref="MyFlags.Flag" />
                public string Name { get; set; }

                [Required]
                public string Description { get; set; }

                public Guid Something { get; set; }
            }
            """,
            """
            using System;
            using System.ComponentModel.DataAnnotations;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyResponseModel
            {
                [Required]
                public string Name { get; set; }

                [Required]
                public string Description { get; set; }

                public Guid Something { get; set; }
            }
            """
        );
    }

    [Fact]
    public async Task EditorConfig_SystemDirectivesNotFirst_SortsAlphabetically()
    {
        // Configure editorconfig to NOT sort System.* directives first
        TestState.AnalyzerConfigFiles.Add(("/.editorconfig", """
            root = true

            [*.cs]
            dotnet_sort_system_directives_first = false
            """));

        await RunDefaultCodeFixAsync(
            """
            using System;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyResponseModel
            {
                /// <required cref="MyFlags.Flag" />
                public string Name { get; set; }
                public Guid Something { get; set; }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;
            using System;
            using System.ComponentModel.DataAnnotations;

            namespace Test;

            public class MyResponseModel
            {
                [Required]
                public string Name { get; set; }
                public Guid Something { get; set; }
            }
            """
        );
    }

    [Fact]
    public async Task EditorConfig_SeparateImportGroups_AddsBlankLinesBetweenGroups()
    {
        // Configure editorconfig to add blank lines between import directive groups
        TestState.AnalyzerConfigFiles.Add(("/.editorconfig", """
            root = true

            [*.cs]
            dotnet_sort_system_directives_first = true
            dotnet_separate_import_directive_groups = true
            """));

        await RunDefaultCodeFixAsync(
            """
            using System;
            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyResponseModel
            {
                /// <required cref="MyFlags.Flag" />
                public string Name { get; set; }
                public Guid Something { get; set; }
            }
            """,
            """
            using System;
            using System.ComponentModel.DataAnnotations;

            using Bitwarden.Server.Sdk.Features;

            namespace Test;

            public class MyResponseModel
            {
                [Required]
                public string Name { get; set; }
                public Guid Something { get; set; }
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
