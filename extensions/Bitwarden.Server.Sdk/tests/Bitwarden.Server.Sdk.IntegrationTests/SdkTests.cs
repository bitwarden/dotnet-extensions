using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Build.Utilities.ProjectCreation;
using Microsoft.Extensions.Logging;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public class SdkTests : MSBuildTestBase
{
    [Fact]
    public void NoOverridingProperties_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(out var result, out var buildOutput)
            .TryGetConstant("BIT_INCLUDE_TELEMETRY", out var hasTelementryConstant)
            .TryGetConstant("BIT_INCLUDE_FEATURES", out var hasFeaturesConstant);

        Assert.True(result, buildOutput.GetConsoleLog());

        Assert.True(hasTelementryConstant);
        Assert.True(hasFeaturesConstant);
    }

    [Fact]
    public void ShouldBuildWithNoWarningsIfProjectHasNullableDisabled()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("Nullable", "disable");
            }
        )
            .TryGetItems("Compile", out var compileItems);

        Assert.True(result, buildOutput.GetConsoleLog());

        Assert.Empty(buildOutput.WarningEvents);
    }

    [Fact]
    public void TelemetryTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeTelemetry", bool.FalseString);
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanCompile()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.FalseString);
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact]
    public void FeaturesTurnedOff_CanNotUseFeatureService()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.FalseString);
            },
            additional: """
                app.MapGet("/test", (Bitwarden.Server.Sdk.Features.IFeatureService featureService) => featureService.GetAll());
                """
        );

        Assert.False(result, buildOutput.GetConsoleLog());

        // error CS0234: The type or namespace name 'Features' does not exist in the namespace 'Bitwarden.Server.Sdk' (are you missing an assembly reference?)
        Assert.Contains(buildOutput.ErrorEvents, e => e.Code == "CS0234");
    }

    [Fact]
    public void FeaturesTurnedOn_CanUseFeatureService()
    {
        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeFeatures", bool.TrueString);
            },
            additional: """
                app.MapGet("/test", (Bitwarden.Server.Sdk.Features.IFeatureService featureService) => featureService.GetAll());
                """
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    public static TheoryData<bool, bool> MatrixData
        => new MatrixTheoryData<bool, bool>([true, false], [true, false]);

    // There will be some variants that disallow the use of feature Y if feature X is not also enabled.
    // Use this set to exclude those known variants from being tested.
    public static HashSet<(bool, bool)> ExcludedVariants => [];

    [Theory, MemberData(nameof(MatrixData))]
    public void AllVariants_Work(bool includeTelemetry, bool includeFeatures)
    {
        if (ExcludedVariants.Contains((includeTelemetry, includeFeatures)))
        {
            Assert.Skip($"""
                Excluded Variant Skipped:
                    IncludeTelemetry = {includeTelemetry}
                    IncludeFeatures = {includeFeatures}
                """);
        }

        ProjectCreator.Templates.SdkProject(
            out var result,
            out var buildOutput,
            customAction: (project) =>
            {
                project.Property("BitIncludeTelemetry", includeTelemetry.ToString());
                project.Property("BitIncludeFeatures", includeFeatures.ToString());
            }
        );

        Assert.True(result, buildOutput.GetConsoleLog());
    }

    [Fact(Skip = "For local development only.")]
    public async Task RunWithTraces()
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XUnitLoggerProvider());
        });

        await using var network = new NetworkBuilder()
            .WithLogger(loggerFactory.CreateLogger("Network"))
            .Build();

        await network.CreateAsync(TestContext.Current.CancellationToken);

        await using var jaegerContainer = new ContainerBuilder()
            .WithImage("jaegertracing/jaeger:2.10.0")
            .WithNetwork(network)
            .WithNetworkAliases("jaeger")
            .WithPortBinding(16686, true)
            .WithLogger(loggerFactory.CreateLogger("Jaeger"))
            .Build();

        await jaegerContainer.StartAsync(TestContext.Current.CancellationToken);

        var project = ProjectCreator.Templates.SdkProject();
        project.AdditionalFile("Program.cs", $$"""
            using System.Diagnostics.Tracing;

            var builder = WebApplication.CreateBuilder();
            builder.UseBitwardenSdk();


            var app = builder.Build();

            app.MapGet("/", () =>
            {
                return Results.Ok();
            });

            app.Run();
            """
        );
        using var packageRepo = project.CreateDefaultPackageRepository();
        project.Save();

        // TODO: This leaves an image on the host machine
        project.TryBuild(
            restore: true,
            targets: ["Publish", "PublishContainer"],
            globalProperties: new Dictionary<string, string>
            {
                { "ContainerRepository", "test-telemetry" },
                { "ContainerFamily", "alpine" },
            },
            out var result, out var buildOutput, out var targetOutputs
        );

        Assert.True(result, buildOutput.GetConsoleLog());

        await using var testContainer = new ContainerBuilder()
            .WithImage("test-telemetry")
            .WithNetwork(network)
            .DependsOn(jaegerContainer)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", $"http://jaeger:4317")
            .WithEnvironment("OTEL_SERVICE_NAME", "test")
            .WithEnvironment("OTEL_DEBUGGING", "true")
            .WithPortBinding(8080, true)
            // This wait strategy both ensures our container full starts up and causes a trace to get created.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080)))
            .WithLogger(loggerFactory.CreateLogger("Example"))
            .Build();

        await testContainer.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TestcontainersStates.Running, testContainer.State);

        var uri = new UriBuilder(
            Uri.UriSchemeHttp,
            jaegerContainer.Hostname,
            jaegerContainer.GetMappedPublicPort(16686),
            "api/v3").Uri;

        using var httpClient = new HttpClient
        {
            BaseAddress = uri,
        };

        await testContainer.StopAsync(TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var traces = await httpClient.GetFromJsonAsync<JsonElement>("traces?service=test", cancellationToken: TestContext.Current.CancellationToken);

        Debug.WriteLine(traces.ToString());

        var (stdout, stderr) = await testContainer.GetLogsAsync(ct: TestContext.Current.CancellationToken);
        Assert.Fail(stdout);

        // Assert that we recieved one trace
        Assert.True(traces.TryGetProperty("data", out var dataProp));
        Assert.Equal(JsonValueKind.Array, dataProp.ValueKind);
        var trace = Assert.Single(dataProp.EnumerateArray());
        Assert.True(trace.TryGetProperty("spans", out var spansProp));
        Assert.Equal(JsonValueKind.Array, spansProp.ValueKind);
        var span = Assert.Single(spansProp.EnumerateArray());
        Assert.True(span.TryGetProperty("operationName", out var operationNameProp));
        Assert.Equal("GET /", operationNameProp.GetString());

        // Uncomment the below lines to view logs from the test container
        // var (stdout, stderr) = await testContainer.GetLogsAsync(ct: TestContext.Current.CancellationToken);
        // Assert.Fail(stdout);
    }
}

internal class XUnitLoggerProvider : ILoggerProvider
{
    private class XUnitLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (TestContext.Current.TestOutputHelper == null)
            {
                return;
            }

            TestContext.Current.TestOutputHelper.WriteLine($"[{category}]: {formatter(state, exception)}");
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(categoryName);
    }

    public void Dispose() { }
}
