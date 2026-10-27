using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Infrastructure.Tests;

public class InfrastructureServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInfrastructureRequirement_RecordsRequirement()
    {
        var services = new ServiceCollection();

        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });

        Assert.Equal("""{"kind":"database","name":"vault"}""", Assert.Single(Collect(services)).ToJsonString());
    }

    [Fact]
    public void AddInfrastructureRequirement_SameRequirementTwice_RecordsOnce()
    {
        var services = new ServiceCollection();

        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });
        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });

        Assert.Single(Collect(services));
    }

    [Fact]
    public void AddInfrastructureRequirement_CollectedTwice_KeepsCollectionsIndependent()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });

        var first = Assert.Single(Collect(services));
        first["name"] = "changed";

        Assert.Equal("vault", Assert.Single(Collect(services))["name"]!.GetValue<string>());
    }

    [Fact]
    public void AddInfrastructureRequirement_PropertiesContainingKind_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddInfrastructureRequirement("database", new JsonObject { ["kind"] = "cache" }));
    }

    [Fact]
    public void AddInfrastructureRequirement_EmptyKind_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddInfrastructureRequirement(""));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureOptions_CanDecideWhetherRequirementExists(bool needsCache)
    {
        var services = new ServiceCollection();
        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });
        services.AddSingleton(new SampleSettings(needsCache));

        services.AddOptions<InfrastructureOptions>()
            .Configure<SampleSettings>((infrastructure, settings) =>
            {
                if (settings.NeedsCache)
                {
                    infrastructure.Add("distributed-cache");
                }
            });

        Assert.Equal(needsCache, Collect(services).Any(r => r["kind"]!.GetValue<string>() == "distributed-cache"));
    }

    private sealed record SampleSettings(bool NeedsCache);

    private static IReadOnlyList<JsonObject> Collect(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements;
    }
}
