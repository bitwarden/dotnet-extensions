using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

/// <summary>
/// The template and the emulator configuration give each entity the settings MessageBroker gives the entities it
/// creates itself (<c>TopologyStartupService</c>), so a topology is the same whoever provisions it.
/// </summary>
public class ServiceBusSettingsTests
{
    private const int MaxDeliveryCount = 7;

    private static readonly string Template = File.ReadAllText(ServiceBusTopologyTests.Infra("message-broker.bicep"));

    [Fact]
    public void TheTemplatesTopicsAreMessageBrokers()
    {
        var expected = TopologyStartupService.TopicOptions("topic");
        var topic = Resource("topic");

        Assert.Equal(Bicep(expected.RequiresDuplicateDetection), Property(topic, "requiresDuplicateDetection"));
        Assert.Equal(Bicep(expected.DuplicateDetectionHistoryTimeWindow), Property(topic, "duplicateDetectionHistoryTimeWindow"));
    }

    [Fact]
    public void TheTemplatesSubscriptionsAreMessageBrokers()
    {
        var expected = TopologyStartupService.SubscriptionOptions("topic", "subscription", MaxDeliveryCount);
        var subscription = Resource("subscription");

        Assert.Equal(MaxDeliveryCount, expected.MaxDeliveryCount);
        Assert.Equal("maxDeliveryCount", Property(subscription, "maxDeliveryCount"));
        Assert.Equal(Bicep(expected.LockDuration), Property(subscription, "lockDuration"));
        Assert.Equal(Bicep(expected.DeadLetteringOnMessageExpiration), Property(subscription, "deadLetteringOnMessageExpiration"));
        Assert.Equal(Bicep(expected.EnableDeadLetteringOnFilterEvaluationExceptions),
            Property(subscription, "deadLetteringOnFilterEvaluationExceptions"));
        // The $Default rule is the family filter MessageBroker makes.
        Assert.Equal([ServiceBusTopology.FamilyProperty], TopologyStartupService.FamilyFilter("family").ApplicationProperties.Keys);
        Assert.Contains($"'{ServiceBusTopology.FamilyProperty}': s.family", Resource("familyFilter"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTemplatesQueuesAreMessageBrokers()
    {
        var expected = TopologyStartupService.QueueOptions("queue", MaxDeliveryCount);
        var queue = Resource("queue");

        Assert.Equal(MaxDeliveryCount, expected.MaxDeliveryCount);
        Assert.Equal("maxDeliveryCount", Property(queue, "maxDeliveryCount"));
        Assert.Equal(Bicep(expected.LockDuration), Property(queue, "lockDuration"));
        Assert.Equal(Bicep(expected.DeadLetteringOnMessageExpiration), Property(queue, "deadLetteringOnMessageExpiration"));
        Assert.Equal(Bicep(expected.RequiresDuplicateDetection), Property(queue, "requiresDuplicateDetection"));
        Assert.Equal(Bicep(expected.DuplicateDetectionHistoryTimeWindow), Property(queue, "duplicateDetectionHistoryTimeWindow"));
    }

    [Fact]
    public void EveryDefaultDeliveryLimitIsMessageBrokers()
    {
        var messageBroker = new MessagingOptions().MaxDeliveryCount;

        Assert.Equal(messageBroker, new ServiceBusOptions().MaxDeliveryCount);
        Assert.Equal(messageBroker, new RabbitDefinitionsOptions().MaxDeliveryCount);
        Assert.Matches($@"(?m)^param maxDeliveryCount int = {messageBroker}$", Template);
    }

    [Fact]
    public void TheEmulatorsSubscriptionsAndQueuesAreMessageBrokers()
    {
        var config = ServiceBusTopology.EmulatorConfig(BrokerTopology.Collect(Sample.Manifests()),
            new ServiceBusOptions { MaxDeliveryCount = MaxDeliveryCount });
        var ns = config["UserConfig"]!["Namespaces"]![0]!;
        var subscription = ns["Topics"]![0]!["Subscriptions"]![0]!["Properties"]!;
        var queue = ns["Queues"]![0]!["Properties"]!;
        var expectedSubscription = TopologyStartupService.SubscriptionOptions("topic", "subscription", MaxDeliveryCount);
        var expectedQueue = TopologyStartupService.QueueOptions("queue", MaxDeliveryCount);

        Assert.Equal(expectedSubscription.MaxDeliveryCount, (int)subscription["MaxDeliveryCount"]!);
        Assert.Equal(XmlConvert.ToString(expectedSubscription.LockDuration), (string)subscription["LockDuration"]!);
        Assert.Equal(expectedSubscription.DeadLetteringOnMessageExpiration, (bool)subscription["DeadLetteringOnMessageExpiration"]!);
        Assert.Equal(expectedQueue.MaxDeliveryCount, (int)queue["MaxDeliveryCount"]!);
        Assert.Equal(XmlConvert.ToString(expectedQueue.LockDuration), (string)queue["LockDuration"]!);
        Assert.Equal(expectedQueue.DeadLetteringOnMessageExpiration, (bool)queue["DeadLetteringOnMessageExpiration"]!);
        Assert.Equal(expectedQueue.RequiresDuplicateDetection, (bool)queue["RequiresDuplicateDetection"]!);
    }

    // A resource's declaration in the template, up to the next one.
    private static string Resource(string name)
    {
        var start = Template.IndexOf($"\nresource {name} ", StringComparison.Ordinal);
        Assert.True(start >= 0, $"message-broker.bicep has no resource {name}.");
        var end = Regex.Match(Template[(start + 1)..], @"\n(resource|output) ").Index;
        return Template.Substring(start, end + 1);
    }

    private static string Property(string resource, string name) =>
        Regex.Match(resource, $@"(?m)^\s*{name}: (.+?)\s*$") is { Success: true } match
            ? match.Groups[1].Value
            : throw new Xunit.Sdk.XunitException($"The resource has no {name}: {resource}");

    private static string Bicep(bool value) => value ? "true" : "false";

    private static string Bicep(TimeSpan value) => $"'{XmlConvert.ToString(value)}'";
}
