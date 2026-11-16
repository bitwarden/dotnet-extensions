using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class InfrastructureRequirementTests
{
    [Fact]
    public void EachRegistrationDeclaresTheTopologyItNeeds()
    {
        var services = new ServiceCollection();
        services.AddPublisher<UserCreatedPayload, UserCreated>("user.events");
        services.AddPublisher<UserDeletedPayload, UserDeleted>("user.events");
        services.AddSubscriber<UserDeletedPayload, UserDeleted>("user.events", "vault");
        services.AddCommandSender<SendMailPayload, SendMail>("notification.commands");
        services.AddCommandConsumer<SendPushPayload, SendPush, SendPushConsumer>("vault.commands");
        services.AddCommandConsumer<SendMailPayload, SendMail, SendMailConsumer>("vault.commands");

        var requirements = services.BuildServiceProvider().GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements;

        Assert.Equal(["user.events"], Values(requirements, MessagingRequirements.Topic, "topic"));
        Assert.Equal(["user.events user.created", "user.events user.deleted"],
            Values(requirements, MessagingRequirements.Publish, "topic", "family"));
        Assert.Equal(["user.events vault user.deleted"],
            Values(requirements, MessagingRequirements.Subscription, "topic", "subscription", "family"));
        Assert.Equal(["notification.commands send.mail"],
            Values(requirements, MessagingRequirements.CommandSend, "queue", "family"));
        // One queue, however many command families it carries, and each family it accepts.
        Assert.Equal(["vault.commands"], Values(requirements, MessagingRequirements.CommandQueue, "queue"));
        Assert.Equal(["vault.commands send.mail", "vault.commands send.push"],
            Values(requirements, MessagingRequirements.CommandConsume, "queue", "family"));
    }

    private static List<string> Values(IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> requirements, string kind, params string[] keys) =>
        requirements.TryGetValue(kind, out var list)
            ? [.. list.Select(r => string.Join(' ', keys.Select(k => (string)r[k]!))).Order(StringComparer.Ordinal)]
            : [];
}
