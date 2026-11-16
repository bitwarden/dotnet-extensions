using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public class ServiceBusTopologyTests
{
    [Fact]
    public void WritesTheTemplatesParameters()
    {
        var parameters = ServiceBusTopology.BicepParameters(BrokerTopology.Collect(Sample.Manifests()));

        var expected = JsonNode.Parse("""
            {
              "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
              "contentVersion": "1.0.0.0",
              "parameters": {
                "maxDeliveryCount": { "value": 10 },
                "topics": { "value": [ { "name": "identity.events", "owner": "identity" } ] },
                "subscriptions": { "value": [ { "topic": "identity.events", "name": "vault", "family": "user.deleted", "reader": "vault" } ] },
                "commandQueues": { "value": [ { "name": "notification.commands", "owner": "notification" } ] },
                "commandSenders": { "value": [ { "queue": "notification.commands", "service": "vault" } ] }
              }
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, parameters), parameters.ToJsonString());
    }

    [Fact]
    public void EveryParameterIsTheTemplatesAndEveryParameterTheTemplateNeedsIsGiven()
    {
        var template = File.ReadAllText(Infra("message-broker.bicep"));
        var declared = Regex.Matches(template, @"(?m)^param (\w+) (\w+)( =)?")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[3].Success);
        var written = ServiceBusTopology.BicepParameters(BrokerTopology.Collect(Sample.Manifests()))["parameters"]!.AsObject()
            .Select(p => p.Key).ToList();

        Assert.All(written, name => Assert.True(declared.ContainsKey(name), $"message-broker.bicep has no parameter {name}."));
        // What's left without a default is the deployment's to give: the namespace and the identities.
        Assert.Equal(["namespaceName", "principalIds"],
            declared.Where(d => !d.Value && !written.Contains(d.Key)).Select(d => d.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheTemplatesExampleParametersAreWhatItWritesForTheSample()
    {
        // infra/messaging/message-broker.parameters.json shows what a deployment is given: the sample's three services.
        var example = JsonNode.Parse(File.ReadAllText(Infra("message-broker.parameters.json")));

        var written = ServiceBusTopology.BicepParameters(BrokerTopology.Collect(Sample.Manifests()));

        Assert.True(JsonNode.DeepEquals(written, example), written.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void OneSenderRightPerServiceAndQueue()
    {
        var parameters = ServiceBusTopology.BicepParameters(BrokerTopology.Collect(
        [
            Sample.Manifest("notification", Sample.Notification),
            Sample.Manifest("vault", s =>
            {
                s.AddCommandSender<SendMailPayload, SendMail>(Sample.NotificationCommands);
                s.AddCommandSender<SendPushPayload, SendPush>(Sample.NotificationCommands);
            }),
        ]));

        Assert.Single(parameters["parameters"]!["commandSenders"]!["value"]!.AsArray());
    }

    [Fact]
    public void RefusesNamesServiceBusDoesNot()
    {
        var topology = BrokerTopology.Collect(
        [
            Sample.Manifest("identity", s => s.AddPublisher<UserDeletedPayload, UserDeleted>("Shared")),
            Sample.Manifest("vault", s => s.AddSubscriber<UserDeletedPayload, UserDeleted>("Shared", new string('v', 51))),
            Sample.Manifest("notification", s => s.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>("shared")),
            Sample.Manifest("billing", s => s.AddPublisher<UserCreatedPayload, UserCreated>("billing events")),
        ]);

        var problems = Assert.Throws<BrokerTopologyException>(() => ServiceBusTopology.BicepParameters(topology)).Problems;

        Assert.Contains(problems, p => p.StartsWith("The topic billing events isn't a Service Bus topic name", StringComparison.Ordinal));
        Assert.Contains("the topic Shared, the queue shared would be one Service Bus entity: topics and queues share one set of names, without regard to case.", problems);
        Assert.Contains(problems, p => p.StartsWith($"The subscription {new string('v', 51)} on Shared isn't", StringComparison.Ordinal));
        Assert.Equal(problems, Assert.Throws<BrokerTopologyException>(() => ServiceBusTopology.EmulatorConfig(topology)).Problems);
    }

    [Fact]
    public void RefusesSubscriptionsThatDifferByCaseAlone()
    {
        var topology = BrokerTopology.Collect(
        [
            Sample.Manifest("identity", Sample.Identity),
            Sample.Manifest("vault", s => s.AddSubscriber<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents, "Vault")),
            Sample.Manifest("billing", s => s.AddSubscriber<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents, "vault")),
        ]);

        Assert.Equal(["The subscriptions Vault and vault on identity.events would be one Service Bus subscription, without regard to case."],
            Assert.Throws<BrokerTopologyException>(() => ServiceBusTopology.BicepParameters(topology)).Problems);
    }

    [Fact]
    public void TheEmulatorHasTheSameEntitiesFilteredByFamily()
    {
        var config = ServiceBusTopology.EmulatorConfig(BrokerTopology.Collect(Sample.Manifests()), new ServiceBusOptions { MaxDeliveryCount = 3 });

        var ns = config["UserConfig"]!["Namespaces"]![0]!;
        Assert.Equal(ServiceBusTopology.EmulatorNamespace, (string)ns["Name"]!);
        Assert.Equal(Sample.NotificationCommands, (string)ns["Queues"]![0]!["Name"]!);
        Assert.Equal(3, (int)ns["Queues"]![0]!["Properties"]!["MaxDeliveryCount"]!);
        var subscription = ns["Topics"]![0]!["Subscriptions"]![0]!;
        Assert.Equal("vault", (string)subscription["Name"]!);
        Assert.Equal("user.deleted", (string)subscription["Rules"]![0]!["Properties"]!["CorrelationFilter"]!["Properties"]![ServiceBusTopology.FamilyProperty]!);
    }

    internal static string Infra(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "infra", "messaging", file);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"infra/messaging/{file} isn't above the test's directory.");
    }
}
