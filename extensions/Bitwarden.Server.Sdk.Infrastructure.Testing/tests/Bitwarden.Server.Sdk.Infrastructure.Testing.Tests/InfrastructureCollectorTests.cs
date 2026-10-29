namespace Bitwarden.Server.Sdk.Infrastructure.Testing.Tests;

public class InfrastructureCollectorTests
{
    [Fact]
    public void CollectInfrastructure_Cloud_CollectsCloudRequirements()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();

        Assert.Equal(
            [
                """database {"name":"vault"}""",
                """distributed-cache {}""",
                """messaging-subscription {"topic":"orders","subscription":"billing"}""",
                """messaging-topic {"topic":"orders"}""",
                """messaging-topic {"topic":"audit"}""",
            ],
            Describe(manifest));
    }

    [Fact]
    public void CollectInfrastructure_SelfHosted_CollectsSelfHostedRequirements()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = true);

        Assert.Equal(
            [
                """database {"name":"vault"}""",
                """messaging-subscription {"topic":"orders","subscription":"billing"}""",
                """messaging-topic {"topic":"orders"}""",
                """self-signed-certificate {"name":"identity"}""",
            ],
            Describe(manifest));
    }

    [Fact]
    public void CollectInfrastructure_DefaultsToProductionEnvironment()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();

        Assert.DoesNotContain("""database {"name":"development-seed"}""", Describe(manifest));
    }

    [Fact]
    public void CollectInfrastructure_Environment_IsVisibleToStartup()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o => o.Environment = "Development");

        Assert.Contains("""database {"name":"development-seed"}""", Describe(manifest));
    }

    [Fact]
    public void CollectInfrastructure_Configuration_IsVisibleToStartup()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o =>
            o.Configuration["Sample:ExtraDatabase"] = "reporting");

        Assert.Contains("""database {"name":"reporting"}""", Describe(manifest));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CollectInfrastructure_EmptyConfiguration_IsTreatedAsUnset(string? value)
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o =>
            o.Configuration["Sample:ExtraDatabase"] = value);

        Assert.Equal(["""database {"name":"vault"}"""], Describe(manifest).Where(r => r.StartsWith("database ")));
    }

    [Fact]
    public void CollectInfrastructure_SelfHostFlavor_IsVisibleToStartup()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o =>
        {
            o.SelfHosted = true;
            o.SelfHostFlavor = "lite";
        });

        Assert.Contains("""volume {"path":"/etc/bitwarden"}""", Describe(manifest));
    }

    [Fact]
    public void CollectInfrastructure_SelfHostedWithoutFlavor_OmitsFlavorRequirements()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = true);

        Assert.False(manifest.Requirements.ContainsKey("volume"));
    }

    [Fact]
    public void CollectInfrastructure_SelfHostFlavorWithoutSelfHosted_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHostFlavor = "lite"));
    }

    [Fact]
    public void CollectInfrastructure_Cloud_DescribesHostingMode()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>();

        Assert.Equal(InfrastructureManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.False(manifest.SelfHosted);
        Assert.Null(manifest.SelfHostFlavor);
        Assert.Equal("Production", manifest.Environment);
    }

    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("lite", "lite")]
    public void CollectInfrastructure_SelfHosted_DescribesFlavor(string? flavor, string expectedFlavor)
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o =>
        {
            o.SelfHosted = true;
            o.SelfHostFlavor = flavor;
        });

        Assert.True(manifest.SelfHosted);
        Assert.Equal(expectedFlavor, manifest.SelfHostFlavor);
    }

    [Fact]
    public void ToJsonString_WritesEnvelope()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = true);

        Assert.Equal(
            """{"schemaVersion":1,"selfHosted":true,"selfHostFlavor":"unknown","environment":"Production","requirements":{"database":[{"name":"vault"}],"messaging-subscription":[{"topic":"orders","subscription":"billing"}],"messaging-topic":[{"topic":"orders"}],"self-signed-certificate":[{"name":"identity"}]}}""",
            manifest.ToJsonString(indented: false));
    }

    [Fact]
    public void ToJsonString_DoesNotIncludeConfiguration()
    {
        var manifest = InfrastructureCollector.CollectInfrastructure<Program>(o =>
            o.Configuration["Sample:Secret"] = "do-not-write");

        Assert.DoesNotContain("do-not-write", manifest.ToJsonString());
    }

    private static IEnumerable<string> Describe(InfrastructureManifest manifest)
        => manifest.Requirements.SelectMany(pair => pair.Value.Select(requirement => $"{pair.Key} {requirement.ToJsonString()}"));
}
