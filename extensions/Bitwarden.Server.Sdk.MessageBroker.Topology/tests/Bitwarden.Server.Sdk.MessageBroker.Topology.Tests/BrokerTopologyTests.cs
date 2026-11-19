using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology.Tests;

public class BrokerTopologyTests
{
    [Fact]
    public void CollectsWhoOwnsReadsAndSendsWhat()
    {
        var topology = BrokerTopology.Collect(Sample.Manifests());

        var topic = Assert.Single(topology.Topics);
        Assert.Equal(Sample.IdentityEvents, topic.Name);
        Assert.Equal("identity", topic.Owner);
        Assert.Equal(["user.created", "user.deleted"], topic.Families);
        Assert.Equal([new SubscriptionTopology("vault", "user.deleted", "vault")], topic.Subscriptions);

        var queue = Assert.Single(topology.CommandQueues);
        Assert.Equal(Sample.NotificationCommands, queue.Name);
        Assert.Equal("notification", queue.Owner);
        Assert.Equal(["send.mail", "send.push"], queue.Families);
        Assert.Equal([new CommandSenderTopology("vault", "send.mail")], queue.Senders);
    }

    [Fact]
    public void ATopicOrQueueHasOneOwner()
    {
        // Vault's subscription and command are reported through their topic's and queue's owners alone, not as having none.
        var problems = Problems(
            Sample.Manifest("identity", s => { Sample.Identity(s); Sample.Notification(s); }),
            Sample.Manifest("impostor", s => { Sample.Identity(s); Sample.Notification(s); }),
            Sample.Manifest("vault", Sample.Vault));

        Assert.Equal(
        [
            "The topic identity.events is claimed by identity and impostor; it has one owner.",
            "The command queue notification.commands is claimed by identity and impostor; it has one owner.",
        ], problems);
    }

    [Fact]
    public void ASubscriptionHasOneReader()
    {
        var problems = Problems(
            Sample.Manifest("identity", Sample.Identity),
            Sample.Manifest("vault", s => s.AddSubscriber<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents, "shared")),
            Sample.Manifest("billing", s => s.AddSubscriber<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents, "shared")));

        Assert.Equal(["Subscription shared on identity.events is read by billing and vault; each needs its own."], problems);
    }

    [Fact]
    public void ASubscriptionReadsOneFamily()
    {
        var problems = Problems(
            Sample.Manifest("identity", Sample.Identity),
            ServiceManifest.Parse("vault", """
                {
                  "schemaVersion": 1,
                  "requirements": {
                    "messaging-subscription": [
                      { "topic": "identity.events", "subscription": "vault", "family": "user.deleted" },
                      { "topic": "identity.events", "subscription": "vault", "family": "user.created" }
                    ]
                  }
                }
                """));

        Assert.Equal(["vault's subscription vault on identity.events reads user.created and user.deleted; a subscription reads one family."], problems);
    }

    [Fact]
    public void ASubscriptionIsToWhatItsTopicsOwnerPublishes()
    {
        var problems = Problems(
            Sample.Manifest("identity", s => s.AddPublisher<UserCreatedPayload, UserCreated>(Sample.IdentityEvents)),
            Sample.Manifest("vault", s =>
            {
                s.AddSubscriber<UserDeletedPayload, UserDeleted>(Sample.IdentityEvents, "vault");
                s.AddSubscriber<UserDeletedPayload, UserDeleted>("billing.events", "vault");
            }));

        Assert.Equal(
        [
            "vault subscribes to user.deleted on identity.events, which identity doesn't publish.",
            "vault subscribes to billing.events, which no service publishes.",
        ], problems);
    }

    [Fact]
    public void ACommandIsSentToAQueueThatConsumesIt()
    {
        var problems = Problems(
            Sample.Manifest("notification", s => s.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>(Sample.NotificationCommands)),
            Sample.Manifest("vault", s =>
            {
                s.AddCommandSender<SendMailPayload, SendMail>(Sample.NotificationCommands);
                s.AddCommandSender<SendMailPayload, SendMail>("billing.commands");
            }));

        Assert.Equal(
        [
            "vault sends send.mail to notification.commands, which notification doesn't consume.",
            "vault sends send.mail to billing.commands, which no service reads.",
        ], problems);
    }

    [Fact]
    public void OnlyATopicsOrQueuesOwnerSaysWhatItCarries()
    {
        var problems = Problems(
            Sample.Manifest("identity", Sample.Identity),
            Sample.Manifest("notification", Sample.Notification),
            ServiceManifest.Parse("rogue", """
                {
                  "schemaVersion": 1,
                  "requirements": {
                    "messaging-publish": [
                      { "topic": "identity.events", "family": "user.renamed" },
                      { "topic": "orphan.events", "family": "user.renamed" }
                    ],
                    "messaging-command-consume": [ { "queue": "notification.commands", "family": "send.sms" } ]
                  }
                }
                """));

        Assert.Equal(
        [
            "rogue publishes user.renamed on the topic identity.events, which identity owns.",
            "rogue publishes user.renamed on the topic orphan.events, which it doesn't declare as its own.",
            "rogue consumes send.sms on the command queue notification.commands, which notification owns.",
        ], problems);
    }

    [Fact]
    public void AServiceHasOneManifest()
    {
        var problems = Problems(Sample.Manifest("identity", Sample.Identity), Sample.Manifest("identity", Sample.Identity));

        Assert.Contains("identity has more than one manifest.", problems);
    }

    private static IReadOnlyList<string> Problems(params ServiceManifest[] manifests) =>
        Assert.Throws<BrokerTopologyException>(() => BrokerTopology.Collect(manifests)).Problems;
}
