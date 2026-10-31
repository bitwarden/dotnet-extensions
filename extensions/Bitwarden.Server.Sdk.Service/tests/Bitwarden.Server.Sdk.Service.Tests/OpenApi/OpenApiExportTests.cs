using System.Text.Json;
using Bitwarden.Server.Sdk.Service.JsonApi;
using Bitwarden.Server.Sdk.Service.OpenApi;
using Bitwarden.Server.Sdk.Service.Tests.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Bitwarden.Server.Sdk.Service.Tests.OpenApi;

/// <summary>
/// A service asked for its contract on the command line writes it to the path given, as an
/// OpenAPI 3.0 file, without doing its own work to get there; every section of the contract says
/// what its resources are, and a contract with one that doesn't is not written.
/// </summary>
public sealed class OpenApiExportTests
{
    [Theory]
    [InlineData(new[] { "--emit-openapi", "openapi.json" }, "openapi.json")]
    [InlineData(new[] { "--urls", "http://localhost:5000", "--emit-openapi", "out/v1.json" }, "out/v1.json")]
    public void Requested_SwitchWithAPath_ReturnsThePath(string[] args, string expected)
    {
        Assert.True(OpenApiExport.Requested(args, out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--urls http://localhost:5000")]
    [InlineData("openapi.json --emit-openapi")]
    [InlineData("--EMIT-OPENAPI openapi.json")]
    public void Requested_NoSwitchOrNoPath_IsNotAnExport(string commandLine)
    {
        var args = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.False(OpenApiExport.Requested(args, out var path));
        Assert.Equal(string.Empty, path);
    }

    [Fact]
    public void HostedServices_TakesOnlyTheHostedServices()
    {
        var services = new ServiceCollection();
        services.AddHostedService<Background>();
        services.AddSingleton<Background>();

        var actual = Assert.Single(OpenApiExport.HostedServices(services));

        Assert.Equal(typeof(IHostedService), actual.ServiceType);
    }

    [Fact]
    public void Isolate_DropsTheServicesOwnWorkAndKeepsThePlatforms()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddHostedService<Background>();
        var platform = OpenApiExport.HostedServices(builder.Services);
        builder.Services.AddHostedService<Work>();
        builder.Services.AddSingleton<Background>();

        OpenApiExport.Isolate(builder, platform);

        var hosted = builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList();
        Assert.Equal(platform, hosted);
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(Background));
    }

    // A contract does not depend on the address of anything the service calls.
    [Fact]
    public void Isolate_DropsStartupValidation()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Services.AddOptions<Settings>().Validate(_ => false).ValidateOnStart();
        Assert.Contains(builder.Services, descriptor => descriptor.ServiceType == typeof(IStartupValidator));

        OpenApiExport.Isolate(builder, OpenApiExport.HostedServices(builder.Services));

        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(IStartupValidator));
    }

    [Fact]
    public void Isolate_BindsToAnEphemeralLoopbackPort()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });

        OpenApiExport.Isolate(builder, []);

        Assert.Equal("http://127.0.0.1:0", builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey));
    }

    [Fact]
    public async Task WriteAsync_WritesTheContractIntoANewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "openapi-export-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "contracts", "v1.json");

        try
        {
            await using var app = new Exported(described: true).Build();

            await OpenApiExport.WriteAsync(app, "v1", path, OpenApiSpecVersion.OpenApi3_0);

            var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.EndsWith("}" + System.Environment.NewLine, text, StringComparison.Ordinal);
            Assert.Contains("\n  \"", text.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);

            using var document = JsonDocument.Parse(text);
            Assert.StartsWith("3.0", document.RootElement.GetProperty("openapi").GetString(), StringComparison.Ordinal);
            Assert.Equal("Export Probe", document.RootElement.GetProperty("info").GetProperty("title").GetString());
            Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/spanners/{id}", out _));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WriteAsync_UndescribedSection_RefusesAndWritesNothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "openapi-export-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "v1.json");

        await using var app = new Exported(described: false).Build();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpenApiExport.WriteAsync(app, "v1", path, OpenApiSpecVersion.OpenApi3_0));

        Assert.Contains("Undescribed: Wrenches.", actual.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void RequireTagDescriptions_NothingPublished_Passes()
    {
        OpenApiExport.RequireTagDescriptions(new OpenApiDocument { Paths = null!, Tags = null });
    }

    [Fact]
    public void RequireTagDescriptions_EverySectionDescribed_Passes()
    {
        var document = Document(("Folders", "The folders a person sorts their items into."));

        OpenApiExport.RequireTagDescriptions(document);
    }

    [Fact]
    public void RequireTagDescriptions_SectionWithoutOne_RefusesNamingIt()
    {
        var document = Document(("Folders", "The folders a person sorts their items into."), ("Ciphers", null));

        var actual = Assert.Throws<InvalidOperationException>(() => OpenApiExport.RequireTagDescriptions(document));

        Assert.Contains("Undescribed: Ciphers.", actual.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Folders", actual.Message, StringComparison.Ordinal);
    }

    // The platform's probes are on every service, and no service's endpoints class can describe them.
    [Fact]
    public async Task OpenApi_SystemSection_IsDescribedByThePlatform()
    {
        await using var service = await SessionHost.StartAsync(productService: false);

        using var response = await service.Client().GetAsync("/api/openapi.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var system = document.RootElement.GetProperty("tags").EnumerateArray()
            .Single(tag => tag.GetProperty("name").GetString() == "System");
        Assert.False(string.IsNullOrWhiteSpace(system.GetProperty("description").GetString()));
    }

    private static OpenApiDocument Document(params (string Name, string? Description)[] sections)
    {
        var document = new OpenApiDocument { Paths = [], Tags = new HashSet<OpenApiTag>() };

        foreach (var (name, description) in sections)
        {
            document.Tags.Add(new OpenApiTag { Name = name, Description = description });

            var operation = new OpenApiOperation { Tags = new HashSet<OpenApiTagReference> { new(name, document) } };
            document.Paths[$"/api/v1/{name.ToLowerInvariant()}"] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
            };
        }

        return document;
    }

    private sealed class Background : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class Work : BackgroundService
    {
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    private sealed class Settings;

    private sealed class Exported(bool described) : Service("export-probe", "Export Probe", [])
    {
        protected override WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            return builder;
        }

        protected override void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGroup("api/v1/spanners").WithStandards().MapGet("{id}", SpannersEndpoints.GetSpanner);

            if (!described)
            {
                endpoints.MapGroup("api/v1/wrenches").WithStandards().MapGet("{id}", WrenchesEndpoints.GetWrench);
            }
        }
    }

    /// <summary>
    /// A resource in a described section.
    /// </summary>
    public sealed record Spanner : Resource;

    /// <summary>
    /// A resource in a section nobody described.
    /// </summary>
    public sealed record Wrench : Resource;

    /// <summary>
    /// A described section.
    /// </summary>
    [ApiTag(Description = "Spanners, described for the tests.")]
    public static class SpannersEndpoints
    {
        /// <summary>
        /// Read a spanner
        /// </summary>
        [StandardReadOne]
        public static ResourceDocument<Spanner> GetSpanner(string id) => StandardResponses.ReadOneResponse(new Spanner { Id = id });
    }

    /// <summary>
    /// A section with no description.
    /// </summary>
    public static class WrenchesEndpoints
    {
        /// <summary>
        /// Read a wrench
        /// </summary>
        [StandardReadOne]
        public static ResourceDocument<Wrench> GetWrench(string id) => StandardResponses.ReadOneResponse(new Wrench { Id = id });
    }
}
