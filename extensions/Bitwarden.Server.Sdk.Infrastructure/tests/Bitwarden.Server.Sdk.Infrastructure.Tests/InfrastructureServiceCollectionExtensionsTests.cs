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

        var requirements = Collect(services);
        Assert.Equal("database", Assert.Single(requirements.Keys));
        Assert.Equal("""{"name":"vault"}""", Assert.Single(requirements["database"]).ToJsonString());
    }

    [Fact]
    public void AddInfrastructureRequirement_SameRequirementTwice_RecordsOnce()
    {
        var services = new ServiceCollection();

        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });
        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });

        Assert.Single(Collect(services)["database"]);
    }

    [Fact]
    public void AddInfrastructureRequirement_CollectedTwice_KeepsCollectionsIndependent()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureRequirement("database", new JsonObject { ["name"] = "vault" });

        var first = Assert.Single(Collect(services)["database"]);
        first["name"] = "changed";

        Assert.Equal("vault", Assert.Single(Collect(services)["database"])["name"]!.GetValue<string>());
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

        Assert.Equal(needsCache, Collect(services).ContainsKey("distributed-cache"));
    }

    private sealed record SampleSettings(bool NeedsCache);

    private static IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> Collect(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements;
    }
}
