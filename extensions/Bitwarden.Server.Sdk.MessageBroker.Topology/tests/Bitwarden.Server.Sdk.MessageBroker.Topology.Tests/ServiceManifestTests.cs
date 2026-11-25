namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public class ServiceManifestTests
{
    [Fact]
    public void ReadsAManifestsJson()
    {
        const string Json = """
            {
              "schemaVersion": 1,
              "selfHosted": false,
              "selfHostFlavor": null,
              "environment": "Production",
              "requirements": {
                "messaging-publish": [ { "topic": "identity.events", "family": "user.deleted" } ],
                "messaging-topic": [ { "topic": "identity.events" } ],
                "something-else": [ { "name": "ignored" } ]
              }
            }
            """;

        var topology = BrokerTopology.Collect([ServiceManifest.Parse("identity", Json)]);

        Assert.Equal(["user.deleted"], Assert.Single(topology.Topics).Families);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "requirements": {} }""")]
    [InlineData("""{ "requirements": {} }""")]
    [InlineData("""[]""")]
    [InlineData("""{ "schemaVersion": 1, "requirements": { "messaging-topic": {} } }""")]
    public void RefusesWhatIsNotAManifest(string json) =>
        Assert.Throws<FormatException>(() => ServiceManifest.Parse("identity", json));

    [Fact]
    public void RefusesARequirementMissingAProperty()
    {
        var manifest = ServiceManifest.Parse("identity", """{ "schemaVersion": 1, "requirements": { "messaging-topic": [ {} ] } }""");

        Assert.Throws<FormatException>(() => BrokerTopology.Collect([manifest]));
    }
}
