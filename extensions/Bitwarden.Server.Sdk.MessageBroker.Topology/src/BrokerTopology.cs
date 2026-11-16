using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// Every service's topics, subscriptions and command queues, collected from their manifests and checked as
/// BROKER-TOPOLOGY.md requires before anything is provisioned or granted. Broker-neutral: the broker's own names
/// and settings come from <see cref="RabbitDefinitions"/> and the other writers.
/// </summary>
public sealed class BrokerTopology
{
    private BrokerTopology(IReadOnlyList<TopicTopology> topics, IReadOnlyList<CommandQueueTopology> commandQueues)
    {
        Topics = topics;
        CommandQueues = commandQueues;
    }

    /// <summary>
    /// Every topic, in ordinal order of name.
    /// </summary>
    public IReadOnlyList<TopicTopology> Topics { get; }

    /// <summary>
    /// Every command queue, in ordinal order of name.
    /// </summary>
    public IReadOnlyList<CommandQueueTopology> CommandQueues { get; }

    /// <summary>
    /// Collects and checks every service's messaging requirements.
    /// </summary>
    /// <param name="services">Each service's manifest, one per service.</param>
    /// <exception cref="BrokerTopologyException">
    /// The services' requirements don't fit together; its <see cref="BrokerTopologyException.Problems"/> lists every
    /// reason, not just the first.
    /// </exception>
    public static BrokerTopology Collect(IEnumerable<ServiceManifest> services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var manifests = services.ToList();
        var problems = new List<string>();

        foreach (var name in manifests.GroupBy(m => m.Service, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            problems.Add($"{name} has more than one manifest.");
        }

        // One owner per topic and per queue: the owner alone may publish to a topic, and read a queue.
        var topicOwners = Owners(manifests, MessagingRequirements.Topic, "topic", "topic", problems);
        var queueOwners = Owners(manifests, MessagingRequirements.CommandQueue, "queue", "command queue", problems);

        var published = OwnedPairs(manifests, MessagingRequirements.Publish, "topic", topicOwners, "publishes", "topic", problems);
        var consumed = OwnedPairs(manifests, MessagingRequirements.CommandConsume, "queue", queueOwners, "consumes", "command queue", problems);

        var subscriptions = new Dictionary<(string Topic, string Subscription), List<(string Service, string Family)>>();
        foreach (var manifest in manifests)
        {
            foreach (var requirement in manifest.Of(MessagingRequirements.Subscription, "topic", "subscription", "family"))
            {
                var key = (requirement["topic"], requirement["subscription"]);
                if (!subscriptions.TryGetValue(key, out var readers))
                {
                    subscriptions[key] = readers = [];
                }

                readers.Add((manifest.Service, requirement["family"]));
            }
        }

        var topics = new Dictionary<string, List<SubscriptionTopology>>(StringComparer.Ordinal);
        foreach (var ((topic, subscription), readers) in subscriptions)
        {
            var readerServices = readers.Select(r => r.Service).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var families = readers.Select(r => r.Family).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (readerServices.Count > 1)
            {
                // Receive rights on one subscription would let each read the other's messages.
                problems.Add($"Subscription {subscription} on {topic} is read by {string.Join(" and ", readerServices)}; each needs its own.");
                continue;
            }

            if (families.Count > 1)
            {
                // A subscription is bound or filtered to one family; the subscriber dead-letters any other.
                problems.Add($"{readerServices[0]}'s subscription {subscription} on {topic} reads {string.Join(" and ", families)}; a subscription reads one family.");
                continue;
            }

            if (!topicOwners.TryGetValue(topic, out var owner))
            {
                problems.Add($"{readerServices[0]} subscribes to {topic}, which no service publishes.");
                continue;
            }

            if (!published.Contains((topic, families[0])))
            {
                problems.Add($"{readerServices[0]} subscribes to {families[0]} on {topic}, which {owner} doesn't publish.");
                continue;
            }

            if (!topics.TryGetValue(topic, out var list))
            {
                topics[topic] = list = [];
            }

            list.Add(new SubscriptionTopology(subscription, families[0], readerServices[0]));
        }

        var senders = new Dictionary<string, List<CommandSenderTopology>>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            foreach (var requirement in manifest.Of(MessagingRequirements.CommandSend, "queue", "family"))
            {
                var (queue, family) = (requirement["queue"], requirement["family"]);
                if (!queueOwners.TryGetValue(queue, out var owner))
                {
                    problems.Add($"{manifest.Service} sends {family} to {queue}, which no service reads.");
                    continue;
                }

                if (!consumed.Contains((queue, family)))
                {
                    // It would be dead-lettered at runtime.
                    problems.Add($"{manifest.Service} sends {family} to {queue}, which {owner} doesn't consume.");
                    continue;
                }

                if (!senders.TryGetValue(queue, out var list))
                {
                    senders[queue] = list = [];
                }

                list.Add(new CommandSenderTopology(manifest.Service, family));
            }
        }

        if (problems.Count > 0)
        {
            throw new BrokerTopologyException(problems);
        }

        return new BrokerTopology(
            [.. topicOwners.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => new TopicTopology(
                t.Key,
                t.Value,
                [.. published.Where(p => p.Destination == t.Key).Select(p => p.Family).Order(StringComparer.Ordinal)],
                [.. (topics.TryGetValue(t.Key, out var list) ? list : []).OrderBy(s => s.Name, StringComparer.Ordinal)]))],
            [.. queueOwners.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => new CommandQueueTopology(
                q.Key,
                q.Value,
                [.. consumed.Where(c => c.Destination == q.Key).Select(c => c.Family).Order(StringComparer.Ordinal)],
                [.. (senders.TryGetValue(q.Key, out var list) ? list : [])
                    .OrderBy(s => s.Service, StringComparer.Ordinal).ThenBy(s => s.Family, StringComparer.Ordinal)]))]);
    }

    private static Dictionary<string, string> Owners(
        List<ServiceManifest> manifests, string kind, string property, string what, List<string> problems)
    {
        var claims = manifests
            .SelectMany(m => m.Of(kind, property).Select(r => (Name: r[property], m.Service)))
            .GroupBy(c => c.Name, StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            var services = claim.Select(c => c.Service).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (services.Count > 1)
            {
                problems.Add($"The {what} {claim.Key} is claimed by {string.Join(" and ", services)}; it has one owner.");
                continue;
            }

            owners[claim.Key] = services[0];
        }

        return owners;
    }

    // What is published on each topic (or consumed from each queue): only its owner may declare it, as MessageBroker's
    // registrations do, so another service's declaration is a problem rather than a family the owner seems to have.
    private static HashSet<(string Destination, string Family)> OwnedPairs(
        List<ServiceManifest> manifests, string kind, string destination, Dictionary<string, string> owners, string verb, string what,
        List<string> problems)
    {
        var pairs = new HashSet<(string Destination, string Family)>();
        foreach (var manifest in manifests)
        {
            foreach (var requirement in manifest.Of(kind, destination, "family"))
            {
                var (name, family) = (requirement[destination], requirement["family"]);
                if (!owners.TryGetValue(name, out var owner))
                {
                    // Unless it's claimed by two, in which case that's the problem already reported.
                    if (!manifests.Any(m => m.Of(OwnerKind(kind), destination).Any(r => r[destination] == name)))
                    {
                        problems.Add($"{manifest.Service} {verb} {family} on the {what} {name}, which it doesn't declare as its own.");
                    }

                    continue;
                }

                if (owner != manifest.Service)
                {
                    problems.Add($"{manifest.Service} {verb} {family} on the {what} {name}, which {owner} owns.");
                    continue;
                }

                pairs.Add((name, family));
            }
        }

        return pairs;
    }

    private static string OwnerKind(string kind) =>
        kind == MessagingRequirements.Publish ? MessagingRequirements.Topic : MessagingRequirements.CommandQueue;
}
