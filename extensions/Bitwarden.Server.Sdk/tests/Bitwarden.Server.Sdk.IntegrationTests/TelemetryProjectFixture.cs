
using DotNet.Testcontainers.Containers;
using Microsoft.Build.Utilities.ProjectCreation;

namespace Bitwarden.Server.Sdk.IntegrationTests;

public sealed class TelemetryProjectFixture : MSBuildTestBase
{
    public const string ImageName = "test-telemetry";
    public ProjectCreator Project { get; }

    public TelemetryProjectFixture()
    {
        var project = ProjectCreator.Templates.SdkProject();
        // Include a label that will make this image get auto cleaned up by test containers
        project.ItemInclude("ContainerLabel", ResourceReaper.ResourceReaperSessionLabel, metadata: new Dictionary<string, string?>
        {
            { "Value", ResourceReaper.DefaultSessionId.ToString("D") },
        });
        project.AdditionalFile("Program.cs", /* lang=c# */ """
            using System.Diagnostics;
            using System.Diagnostics.Tracing;
            using System.Diagnostics.Metrics;
            using Microsoft.AspNetCore.Http.Features;
            using ZiggyCreatures.Caching.Fusion;

            var builder = WebApplication.CreateBuilder();
            builder.UseBitwardenSdk();

            builder.Services.AddSingleton<CustomMetrics>();

            var source = new ActivitySource("Bitwarden.MyFeature");

            var app = builder.Build();

            app.MapGet("/", async (
                HttpContext context,
                CustomMetrics metrics,
                IConfiguration configuration
            ) =>
            {
                // Custom trace
                using var activity = source.StartActivity("MyOperation");
                // Add tag to HTTP trace
                context.Features.Get<IHttpActivityFeature>()?.Activity.SetTag("custom_tag", "my_value");
                // Custom metric
                metrics.Test();

                if (configuration["Caching:Redis:Configuration"] != null)
                {
                    var cache = context.RequestServices.GetRequiredKeyedService<IFusionCache>("MyCache");
                    await cache.SetAsync("Key", "Value");
                }

                return Results.Ok();
            });

            app.Run();

            internal sealed class CustomMetrics : IDisposable
            {
                private readonly Meter _meter;
                private readonly Counter<int> _counter;

                public CustomMetrics(IMeterFactory meterFactory)
                {
                    _meter = meterFactory.Create("Bitwarden.Custom");
                    _counter = _meter.CreateCounter<int>("custom_counter");
                }

                public void Test()
                {
                    _counter.Add(1);
                }

                public void Dispose()
                {
                    _meter.Dispose();
                }
            }
            """
        );
        using var packageRepo = project.CreateDefaultPackageRepository();
        project.Save();

        project.TryBuild(
            restore: true,
            targets: ["Publish", "PublishContainer"],
            globalProperties: new Dictionary<string, string>
            {
                { "ContainerRepository", ImageName },
                { "ContainerFamily", "alpine" },
                { "BitIncludeCaching", "true" },
            },
            out var result, out var buildOutput, out var targetOutputs
        );

        Assert.True(result, buildOutput.GetConsoleLog());

        Project = project;
    }
}
