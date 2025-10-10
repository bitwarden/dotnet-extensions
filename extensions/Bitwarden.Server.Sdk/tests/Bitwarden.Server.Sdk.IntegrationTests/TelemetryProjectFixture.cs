
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
        project.AdditionalFile("Program.cs", $$"""
            using System.Diagnostics.Tracing;
            using System.Diagnostics.Metrics;
            using Microsoft.AspNetCore.Http.Features;

            var builder = WebApplication.CreateBuilder();
            builder.UseBitwardenSdk();

            builder.Services.AddSingleton<CustomMetrics>();


            var app = builder.Build();

            app.MapGet("/", (HttpContext context, CustomMetrics metrics) =>
            {
                context.Features.Get<IHttpActivityFeature>()?.Activity.SetTag("custom_tag", "my_value");
                metrics.Test();
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
                { "BitIncludeFeatures", "false" },
            },
            out var result, out var buildOutput, out var targetOutputs
        );

        Assert.True(result, buildOutput.GetConsoleLog());

        Project = project;
    }
}
