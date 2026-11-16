using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public class RabbitDefinitionsTests
{
    [Fact]
    public void WritesTheVirtualHostExchangesQueuesBindingsAndPolicies()
    {
        var definitions = RabbitDefinitions.Build(BrokerTopology.Collect(Sample.Manifests()));

        var expected = JsonNode.Parse("""
            {
              "vhosts": [ { "name": "/" } ],
              "exchanges": [
                { "name": "dead-letter.identity.events.vault", "vhost": "/", "type": "fanout", "durable": true, "auto_delete": false, "internal": false, "arguments": {} },
                { "name": "dead-letter.notification.commands", "vhost": "/", "type": "fanout", "durable": true, "auto_delete": false, "internal": false, "arguments": {} },
                { "name": "identity.events", "vhost": "/", "type": "topic", "durable": true, "auto_delete": false, "internal": false, "arguments": {} },
                { "name": "notification.commands", "vhost": "/", "type": "direct", "durable": true, "auto_delete": false, "internal": false, "arguments": {} }
              ],
              "queues": [
                { "name": "dead-letter.identity.events.vault", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
                { "name": "dead-letter.notification.commands", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
                { "name": "identity.events.vault", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } },
                { "name": "notification.commands", "vhost": "/", "durable": true, "auto_delete": false, "arguments": { "x-queue-type": "quorum" } }
              ],
              "bindings": [
                { "source": "dead-letter.identity.events.vault", "vhost": "/", "destination": "dead-letter.identity.events.vault", "destination_type": "queue", "routing_key": "", "arguments": {} },
                { "source": "dead-letter.notification.commands", "vhost": "/", "destination": "dead-letter.notification.commands", "destination_type": "queue", "routing_key": "", "arguments": {} },
                { "source": "identity.events", "vhost": "/", "destination": "identity.events.vault", "destination_type": "queue", "routing_key": "user.deleted", "arguments": {} },
                { "source": "notification.commands", "vhost": "/", "destination": "notification.commands", "destination_type": "queue", "routing_key": "notification.commands", "arguments": {} }
              ],
              "policies": [
                {
                  "vhost": "/", "name": "identity.events.vault", "pattern": "^identity\\.events\\.vault$", "apply-to": "queues", "priority": 0,
                  "definition": { "delivery-limit": 9, "dead-letter-exchange": "dead-letter.identity.events.vault", "dead-letter-strategy": "at-least-once", "overflow": "reject-publish" }
                },
                {
                  "vhost": "/", "name": "notification.commands", "pattern": "^notification\\.commands$", "apply-to": "queues", "priority": 0,
                  "definition": { "delivery-limit": 9, "dead-letter-exchange": "dead-letter.notification.commands", "dead-letter-strategy": "at-least-once", "overflow": "reject-publish" }
                }
              ]
            }
            """);
        Assert.True(JsonNode.DeepEquals(expected, definitions), definitions.ToJsonString());
    }

    [Fact]
    public void EachSubscriptionDeadLettersToItsOwnExchangeAndQueue()
    {
        var definitions = RabbitDefinitions.Build(BrokerTopology.Collect(
        [
            Sample.Manifest("identity", Sample.Identity),
            Sample.Manifest("vault", s => s.AddSubscriber<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents, "vault")),
            Sample.Manifest("billing", s => s.AddSubscriber<UserCreatedPayload, UserCreated>(Sample.IdentityEvents, "billing")),
        ]));

        Assert.Equal(
            ["dead-letter.identity.events.billing", "dead-letter.identity.events.vault", "identity.events.billing", "identity.events.vault"],
            Names(definitions, "queues"));
        Assert.Equal(
            [("identity.events.billing", "dead-letter.identity.events.billing"), ("identity.events.vault", "dead-letter.identity.events.vault")],
            definitions["policies"]!.AsArray().Select(p => ((string)p!["name"]!, (string)p["definition"]!["dead-letter-exchange"]!)));
    }

    [Fact]
    public void ATopicNobodySubscribesToHasNoDeadLetters()
    {
        var definitions = RabbitDefinitions.Build(BrokerTopology.Collect([Sample.Manifest("identity", Sample.Identity)]));

        Assert.Equal(["identity.events"], Names(definitions, "exchanges"));
        Assert.Empty(Names(definitions, "queues"));
        Assert.Empty(definitions["policies"]!.AsArray());
    }

    [Fact]
    public void TheDeliveryLimitCountsRedeliveriesOnly()
    {
        var definitions = RabbitDefinitions.Build(BrokerTopology.Collect(Sample.Manifests()),
            new RabbitDefinitionsOptions { MaxDeliveryCount = 3, VirtualHost = "messaging" });

        Assert.Equal(["messaging"], Names(definitions, "vhosts"));
        Assert.All(definitions["policies"]!.AsArray(), policy =>
        {
            Assert.Equal(2, (int)policy!["definition"]!["delivery-limit"]!);
            Assert.Equal("messaging", (string)policy["vhost"]!);
        });
    }

    [Fact]
    public void RefusesFewerThanTwoDeliveries() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RabbitDefinitions.Build(
            BrokerTopology.Collect(Sample.Manifests()), new RabbitDefinitionsOptions { MaxDeliveryCount = 1 }));

    [Fact]
    public void RefusesTwoDeclarationsThatMakeOneName()
    {
        var topology = BrokerTopology.Collect(
        [
            // The subscription vault on notification and the command queue notification.vault are one queue.
            Sample.Manifest("notification", s =>
            {
                s.AddPublisher<UserCreatedPayload, UserCreated>("notification");
                s.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>("notification.vault");
            }),
            Sample.Manifest("vault", s => s.AddSubscriber<UserCreatedPayload, UserCreated>("notification", "vault")),
            // So are the subscription b.c on a and the subscription c on a.b.
            Sample.Manifest("a", s => s.AddPublisher<UserCreatedPayload, UserCreated>("a")),
            Sample.Manifest("ab", s => s.AddPublisher<UserCreatedPayload, UserCreated>("a.b")),
            Sample.Manifest("first", s => s.AddSubscriber<UserCreatedPayload, UserCreated>("a", "b.c")),
            Sample.Manifest("second", s => s.AddSubscriber<UserCreatedPayload, UserCreated>("a.b", "c")),
            // And a topic's exchange and a command queue's.
            Sample.Manifest("billing", s => s.AddPublisher<UserCreatedPayload, UserCreated>("billing.inbox")),
            Sample.Manifest("mail", s => s.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("billing.inbox")),
        ]);

        var problems = Assert.Throws<BrokerTopologyException>(() => RabbitDefinitions.Build(topology)).Problems;

        Assert.Equal(
        [
            "The RabbitMQ name a.b.c would be made by the subscription b.c on a and the subscription c on a.b.",
            "The RabbitMQ name dead-letter.a.b.c would be made by the subscription b.c on a and the subscription c on a.b.",
            "The RabbitMQ name billing.inbox would be made by the command queue billing.inbox and the topic billing.inbox.",
            "The RabbitMQ name notification.vault would be made by the command queue notification.vault and the subscription vault on notification.",
            "The RabbitMQ name dead-letter.notification.vault would be made by the command queue notification.vault and the subscription vault on notification.",
        ], problems);
    }

    [Fact]
    public void RefusesTopicsAndQueuesNamedWithTheDeadLetterPart()
    {
        // The command queue x and the subscription x on the topic dead-letter would share dead-letter.x.
        var topology = BrokerTopology.Collect(
        [
            Sample.Manifest("owner", s => s.AddPublisher<UserCreatedPayload, UserCreated>("dead-letter")),
            Sample.Manifest("reader", s => s.AddSubscriber<UserCreatedPayload, UserCreated>("dead-letter", "x")),
            Sample.Manifest("queue", s =>
            {
                s.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("x");
                s.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>("dead-letter.y");
            }),
            // Only the whole first part is reserved.
            Sample.Manifest("letters", s => s.AddPublisher<UserDeletedPayload, UserDeleted>("dead-letters")),
        ]);

        var problems = Assert.Throws<BrokerTopologyException>(() => RabbitDefinitions.Build(topology)).Problems;

        Assert.Contains("The topic dead-letter is named with dead-letter, the first name part reserved for RabbitMQ's dead-letter exchanges and queues.", problems);
        Assert.Contains("The command queue dead-letter.y is named with dead-letter, the first name part reserved for RabbitMQ's dead-letter exchanges and queues.", problems);
        Assert.Contains("The RabbitMQ name dead-letter.x would be made by the command queue x and the subscription x on dead-letter.", problems);
        Assert.DoesNotContain(problems, p => p.Contains("dead-letters", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesNamesLongerThanRabbitMqAllows()
    {
        // The queue fits; its dead-letter queue doesn't.
        var queue = new string('q', 250);
        var topology = BrokerTopology.Collect([Sample.Manifest("notification", s => s.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>(queue))]);

        Assert.Equal([$"The RabbitMQ name dead-letter.{queue} is longer than RabbitMQ's 255 bytes."],
            Assert.Throws<BrokerTopologyException>(() => RabbitDefinitions.Build(topology)).Problems);
    }

    private static List<string> Names(JsonObject definitions, string kind) =>
        [.. definitions[kind]!.AsArray().Select(item => (string)item!["name"]!)];
}
