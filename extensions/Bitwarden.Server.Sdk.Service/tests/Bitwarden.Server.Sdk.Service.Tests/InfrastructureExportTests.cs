using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.Service.Tests;

/// <summary>
/// A service asked for its infrastructure on the command line writes what it declares to the path given, in the
/// manifest shape a test's InfrastructureCollector returns, without starting.
/// </summary>
public sealed class InfrastructureExportTests
{
    [Theory]
    [InlineData(new[] { "--emit-infrastructure", "infrastructure.json" }, "infrastructure.json")]
    [InlineData(new[] { "--urls", "http://localhost:5000", "--emit-infrastructure", "out/i.json" }, "out/i.json")]
    public void Requested_SwitchWithAPath_ReturnsThePath(string[] args, string expected)
    {
        Assert.True(InfrastructureExport.Requested(args, out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--emit-openapi openapi.json")]
    [InlineData("infrastructure.json --emit-infrastructure")]
    public void Requested_NoSwitchOrNoPath_IsNotAnExport(string commandLine)
    {
        Assert.False(InfrastructureExport.Requested(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries), out var path));
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public async Task Run_WritesWhatTheServiceDeclaresWithoutStartingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "infrastructure-export-" + Guid.NewGuid().ToString("N"), "infrastructure.json");
        try
        {
            await Task.Run(() => new Declaring(["--emit-infrastructure", path]).Run(), TestContext.Current.CancellationToken);

            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.EndsWith("}\n", text, StringComparison.Ordinal);
            var expected = JsonNode.Parse("""
                {
                  "schemaVersion": 1,
                  "selfHosted": false,
                  "selfHostFlavor": null,
                  "environment": "Testing",
                  "requirements": {
                    "messaging-publish": [ { "topic": "probe.events", "family": "probe.made" } ],
                    "messaging-topic": [ { "topic": "probe.events" } ]
                  }
                }
                """);
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(text)), text);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task Run_SelfHosted_WritesTheSelfHostedManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), "infrastructure-export-" + Guid.NewGuid().ToString("N"), "infrastructure.json");
        try
        {
            await Task.Run(() => new Declaring(
                ["--emit-infrastructure", path, "--globalSettings:selfHosted=true", "--globalSettings:selfHostFlavor=lite"]).Run(),
                TestContext.Current.CancellationToken);

            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
            Assert.True((bool)manifest["selfHosted"]!);
            Assert.Equal("lite", (string?)manifest["selfHostFlavor"]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private sealed class Declaring(string[] args) : Service("infrastructure-probe", "Infrastructure Probe", args)
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Args, EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddInfrastructureRequirement("messaging-topic", new JsonObject { ["topic"] = "probe.events" });
            services.AddInfrastructureRequirement("messaging-publish", new JsonObject { ["topic"] = "probe.events", ["family"] = "probe.made" });
            // Never started: describing doesn't run the service's work, and writing the manifest doesn't start it.
            services.AddHostedService<Refuses>();
        }

        protected override void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }

    private sealed class Refuses : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Started.");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
