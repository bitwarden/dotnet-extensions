using System.Diagnostics.CodeAnalysis;
using Bitwarden.Server.Sdk.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Analyzers.Tests;

public class RemoveFeatureFlagCodeFixerTests : CSharpCodeFixTest<FeatureFlagAnalyzer, RemoveFeatureFlagCodeFixer, DefaultVerifier>
{
    [Fact]
    public async Task ReplacesFeatureCheckWithTrueLiteral()
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

    [Fact]
    public async Task ShouldRemoveIfCheck()
    {
        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    if ({|BW0002:featureService.IsEnabled(Flag)|})
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

            public class Something
            {
                private const string Flag = "my-flag";

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
        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    if (!{|BW0002:featureService.IsEnabled(Flag)|})
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

            public class Something
            {
                private const string Flag = "my-flag";

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
        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";

                public Something(IFeatureService featureService)
                {
                    if (!{|BW0002:featureService.IsEnabled(Flag)|})
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

            public class Something
            {
                private const string Flag = "my-flag";

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
    public async Task ShouldRemoveAllFlags()
    {
        CodeActionEquivalenceKey = "BW0002-my-flag";

        // We expect the diagnostic for the flag we didn't fix to still exist
        FixedState.ExpectedDiagnostics.Add(
            new DiagnosticResult("BW0002", DiagnosticSeverity.Info)
                .WithSpan(11, 26, 11, 63)
        );

        await RunCodeFixAsync(
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";
                private const string AnotherFlag = "another-flag";

                public Something(IFeatureService featureService)
                {
                    var isEnabled1 = {|BW0002:featureService.IsEnabled(Flag)|};
                    var isEnabled2 = {|BW0002:featureService.IsEnabled(AnotherFlag)|};
                    var isEnabled3 = {|BW0002:featureService.IsEnabled(Flag)|};
                }
            }
            """,
            """
            using Bitwarden.Server.Sdk.Features;

            public class Something
            {
                private const string Flag = "my-flag";
                private const string AnotherFlag = "another-flag";

                public Something(IFeatureService featureService)
                {
                    var isEnabled1 = true;
                    var isEnabled2 = featureService.IsEnabled(AnotherFlag);
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

        // TODO: Add assemblies
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
    public async Task MinimalApiEndpointMetadata()
    {
        TestState.OutputKind = OutputKind.ConsoleApplication;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(WebApplication).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(EndpointRouteBuilderExtensions).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IApplicationBuilder).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IHost).Assembly.Location));

        await RunCodeFixAsync(
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
    public async Task MinimalApiEndpointMetadata_NotChained()
    {
        TestState.OutputKind = OutputKind.ConsoleApplication;
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(WebApplication).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(EndpointRouteBuilderExtensions).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IApplicationBuilder).Assembly.Location));
        TestState.AdditionalReferences.Add(MetadataReference.CreateFromFile(typeof(IHost).Assembly.Location));

        await RunCodeFixAsync(
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

            app.MapGet("/", () => "Hello world!");

            public static class Flags
            {
                public const string Flag = "my-flag";
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
