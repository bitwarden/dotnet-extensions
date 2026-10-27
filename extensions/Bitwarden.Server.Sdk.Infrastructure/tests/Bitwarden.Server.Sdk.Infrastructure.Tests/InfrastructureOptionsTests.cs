using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Infrastructure.Tests;

public class InfrastructureOptionsTests
{
    [Fact]
    public void Add_WritesKindFirst()
    {
        var options = new InfrastructureOptions();

        options.Add("messaging-subscription", new JsonObject { ["topic"] = "orders", ["subscription"] = "billing" });

        var requirement = Assert.Single(options.Requirements);
        Assert.Equal("""{"kind":"messaging-subscription","topic":"orders","subscription":"billing"}""", requirement.ToJsonString());
    }

    [Fact]
    public void Add_WithoutProperties_WritesOnlyKind()
    {
        var options = new InfrastructureOptions();

        options.Add("distributed-cache");

        Assert.Equal("""{"kind":"distributed-cache"}""", Assert.Single(options.Requirements).ToJsonString());
    }

    [Fact]
    public void Add_DeeplyEqualRequirement_AddsOnce()
    {
        var options = new InfrastructureOptions();

        options.Add("messaging-subscription", new JsonObject { ["topic"] = "orders", ["subscription"] = "billing" });
        options.Add("messaging-subscription", new JsonObject { ["subscription"] = "billing", ["topic"] = "orders" });

        Assert.Single(options.Requirements);
    }

    [Fact]
    public void Add_DifferentProperties_AddsBoth()
    {
        var options = new InfrastructureOptions();

        options.Add("configuration", new JsonObject { ["configurationKey"] = "a" });
        options.Add("configuration", new JsonObject { ["configurationKey"] = "a", ["secret"] = true });

        Assert.Equal(2, options.Requirements.Count);
    }

    [Fact]
    public void Add_SamePropertiesDifferentKind_AddsBoth()
    {
        var options = new InfrastructureOptions();

        options.Add("database", new JsonObject { ["name"] = "vault" });
        options.Add("distributed-cache", new JsonObject { ["name"] = "vault" });

        Assert.Equal(2, options.Requirements.Count);
    }

    [Fact]
    public void Add_PreservesFirstAddedOrder()
    {
        var options = new InfrastructureOptions();

        options.Add("database", new JsonObject { ["name"] = "b" });
        options.Add("database", new JsonObject { ["name"] = "a" });
        options.Add("database", new JsonObject { ["name"] = "b" });

        Assert.Equal(["b", "a"], options.Requirements.Select(r => r["name"]!.GetValue<string>()));
    }

    [Fact]
    public void Add_CopiesProperties()
    {
        var options = new InfrastructureOptions();
        var properties = new JsonObject { ["name"] = "vault", ["tags"] = new JsonArray("a") };

        options.Add("database", properties);
        properties["name"] = "changed";
        properties["tags"]!.AsArray().Add("b");

        Assert.Equal("""{"kind":"database","name":"vault","tags":["a"]}""", Assert.Single(options.Requirements).ToJsonString());
    }

    [Fact]
    public void Add_PropertiesContainingKind_Throws()
    {
        var options = new InfrastructureOptions();

        Assert.Throws<ArgumentException>(() => options.Add("database", new JsonObject { ["kind"] = "cache" }));
    }

    [Fact]
    public void Add_EmptyKind_Throws()
    {
        var options = new InfrastructureOptions();

        Assert.Throws<ArgumentException>(() => options.Add(""));
    }
}
