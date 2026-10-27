namespace Bitwarden.Server.Sdk.Infrastructure.Testing.Tests;

public class InfrastructureCollectorTests
{
    [Fact]
    public void CollectInfrastructure_Cloud_CollectsCloudRequirements()
    {
        var requirements = InfrastructureCollector.CollectInfrastructure<Program>();

        Assert.Equal(
            [
                """{"kind":"database","name":"vault"}""",
                """{"kind":"distributed-cache"}""",
                """{"kind":"messaging-topic","topic":"orders"}""",
                """{"kind":"messaging-subscription","topic":"orders","subscription":"billing"}""",
                """{"kind":"messaging-topic","topic":"audit"}""",
            ],
            requirements.Select(r => r.ToJsonString()));
    }

    [Fact]
    public void CollectInfrastructure_SelfHosted_CollectsSelfHostedRequirements()
    {
        var requirements = InfrastructureCollector.CollectInfrastructure<Program>(o => o.SelfHosted = true);

        Assert.Equal(
            [
                """{"kind":"database","name":"vault"}""",
                """{"kind":"self-signed-certificate","name":"identity"}""",
                """{"kind":"messaging-topic","topic":"orders"}""",
                """{"kind":"messaging-subscription","topic":"orders","subscription":"billing"}""",
            ],
            requirements.Select(r => r.ToJsonString()));
    }

    [Fact]
    public void CollectInfrastructure_DefaultsToProductionEnvironment()
    {
        var requirements = InfrastructureCollector.CollectInfrastructure<Program>();

        Assert.DoesNotContain(requirements, r => r.ToJsonString().Contains("development-seed"));
    }

    [Fact]
    public void CollectInfrastructure_Environment_IsVisibleToStartup()
    {
        var requirements = InfrastructureCollector.CollectInfrastructure<Program>(o => o.Environment = "Development");

        Assert.Contains(requirements, r => r.ToJsonString() == """{"kind":"database","name":"development-seed"}""");
    }

    [Fact]
    public void CollectInfrastructure_Configuration_IsVisibleToStartup()
    {
        var requirements = InfrastructureCollector.CollectInfrastructure<Program>(o =>
            o.Configuration["Sample:ExtraDatabase"] = "reporting");

        Assert.Contains(requirements, r => r.ToJsonString() == """{"kind":"database","name":"reporting"}""");
    }
}
