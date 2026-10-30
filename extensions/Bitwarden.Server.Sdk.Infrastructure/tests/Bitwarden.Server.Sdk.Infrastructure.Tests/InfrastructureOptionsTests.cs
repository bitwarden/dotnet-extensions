using System.Text.Json.Nodes;

namespace Bitwarden.Server.Sdk.Infrastructure.Tests;

public class InfrastructureOptionsTests
{
    [Fact]
    public void Add_GroupsByKind()
    {
        var options = new InfrastructureOptions();

        options.Add("messaging-topic", new JsonObject { ["topic"] = "orders" });
        options.Add("database", new JsonObject { ["name"] = "vault" });
        options.Add("messaging-topic", new JsonObject { ["topic"] = "audit" });

        Assert.Equal(["database", "messaging-topic"], options.Requirements.Keys);
        Assert.Equal(["""{"name":"vault"}"""], Describe(options, "database"));
        Assert.Equal(["""{"topic":"orders"}""", """{"topic":"audit"}"""], Describe(options, "messaging-topic"));
    }

    [Fact]
    public void Add_OrdersKindsOrdinally()
    {
        var options = new InfrastructureOptions();

        options.Add("volume");
        options.Add("Database");
        options.Add("database");

        Assert.Equal(["Database", "database", "volume"], options.Requirements.Keys);
    }

    [Fact]
    public void Add_WithoutProperties_AddsEmptyObject()
    {
        var options = new InfrastructureOptions();

        options.Add("distributed-cache");

        Assert.Equal(["{}"], Describe(options, "distributed-cache"));
    }

    [Fact]
    public void Add_DeeplyEqualProperties_AddsOnce()
    {
        var options = new InfrastructureOptions();

        options.Add("messaging-subscription", new JsonObject { ["topic"] = "orders", ["subscription"] = "billing" });
        options.Add("messaging-subscription", new JsonObject { ["subscription"] = "billing", ["topic"] = "orders" });

        Assert.Single(options.Requirements["messaging-subscription"]);
    }

    [Fact]
    public void Add_DifferentProperties_AddsBoth()
    {
        var options = new InfrastructureOptions();

        options.Add("configuration", new JsonObject { ["configurationKey"] = "a" });
        options.Add("configuration", new JsonObject { ["configurationKey"] = "a", ["secret"] = true });

        Assert.Equal(2, options.Requirements["configuration"].Count);
    }

    [Fact]
    public void Add_SamePropertiesDifferentKind_AddsBoth()
    {
        var options = new InfrastructureOptions();

        options.Add("database", new JsonObject { ["name"] = "vault" });
        options.Add("distributed-cache", new JsonObject { ["name"] = "vault" });

        Assert.Single(options.Requirements["database"]);
        Assert.Single(options.Requirements["distributed-cache"]);
    }

    [Fact]
    public void Add_PreservesFirstAddedOrderWithinKind()
    {
        var options = new InfrastructureOptions();

        options.Add("database", new JsonObject { ["name"] = "b" });
        options.Add("database", new JsonObject { ["name"] = "a" });
        options.Add("database", new JsonObject { ["name"] = "b" });

        Assert.Equal(["""{"name":"b"}""", """{"name":"a"}"""], Describe(options, "database"));
    }

    [Fact]
    public void Add_CopiesProperties()
    {
        var options = new InfrastructureOptions();
        var properties = new JsonObject { ["name"] = "vault", ["tags"] = new JsonArray("a") };

        options.Add("database", properties);
        properties["name"] = "changed";
        properties["tags"]!.AsArray().Add("b");

        Assert.Equal(["""{"name":"vault","tags":["a"]}"""], Describe(options, "database"));
    }

    [Fact]
    public void Add_PropertiesMayContainKind()
    {
        var options = new InfrastructureOptions();

        options.Add("database", new JsonObject { ["kind"] = "sql" });

        Assert.Equal(["""{"kind":"sql"}"""], Describe(options, "database"));
    }

    [Fact]
    public void Requirements_CannotBeModified()
    {
        var options = new InfrastructureOptions();
        options.Add("database", new JsonObject { ["name"] = "vault" });

        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<JsonObject>>)options.Requirements).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<JsonObject>)options.Requirements["database"]).Clear());
    }

    [Fact]
    public void Add_EmptyKind_Throws()
    {
        var options = new InfrastructureOptions();

        Assert.Throws<ArgumentException>(() => options.Add(""));
    }

    private static IEnumerable<string> Describe(InfrastructureOptions options, string kind)
        => options.Requirements[kind].Select(r => r.ToJsonString());
}
