using System.Text.Json.Nodes;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The infrastructure requirement kinds each registration declares through
/// <c>Bitwarden.Server.Sdk.Infrastructure</c>, so the broker topology a service needs can be
/// collected from its startup and provisioned outside it. They name topics, subscriptions, queues
/// and payload families, never a connection string or key.
/// </summary>
public static class MessagingRequirements
{
    /// <summary>A topic the service publishes to and owns: <c>{ "topic" }</c>.</summary>
    public const string Topic = "messaging-topic";

    /// <summary>A payload family the service publishes on a topic: <c>{ "topic", "family" }</c>. Grants the right to send.</summary>
    public const string Publish = "messaging-publish";

    /// <summary>
    /// A subscription the service reads, filtered or bound to one payload family:
    /// <c>{ "topic", "subscription", "family" }</c>. Grants the right to receive from it.
    /// </summary>
    public const string Subscription = "messaging-subscription";

    /// <summary>The service's own command queue, which it owns and reads: <c>{ "queue" }</c>.</summary>
    public const string CommandQueue = "messaging-command-queue";

    /// <summary>
    /// A command family the service accepts on its own queue: <c>{ "queue", "family" }</c>. Lets
    /// provisioning check each <see cref="CommandSend"/> against what the queue's owner consumes.
    /// </summary>
    public const string CommandConsume = "messaging-command-consume";

    /// <summary>A command family the service sends to another service's queue: <c>{ "queue", "family" }</c>. Grants the right to send.</summary>
    public const string CommandSend = "messaging-command-send";

    internal static void DeclarePublisher(IServiceCollection services, string topic, string family)
    {
        services.AddInfrastructureRequirement(Topic, new JsonObject { ["topic"] = topic });
        services.AddInfrastructureRequirement(Publish, new JsonObject { ["topic"] = topic, ["family"] = family });
    }

    internal static void DeclareSubscription(IServiceCollection services, string topic, string subscription, string family) =>
        services.AddInfrastructureRequirement(Subscription,
            new JsonObject { ["topic"] = topic, ["subscription"] = subscription, ["family"] = family });

    internal static void DeclareCommandConsumer(IServiceCollection services, string queue, string family)
    {
        services.AddInfrastructureRequirement(CommandQueue, new JsonObject { ["queue"] = queue });
        services.AddInfrastructureRequirement(CommandConsume, new JsonObject { ["queue"] = queue, ["family"] = family });
    }

    internal static void DeclareCommandSend(IServiceCollection services, string queue, string family) =>
        services.AddInfrastructureRequirement(CommandSend, new JsonObject { ["queue"] = queue, ["family"] = family });
}
