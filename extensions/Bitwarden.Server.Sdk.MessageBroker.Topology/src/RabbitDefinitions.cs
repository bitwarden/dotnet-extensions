using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// A topology as RabbitMQ definitions, for RabbitMQ to import. The virtual host, exchanges, queues,
/// bindings and policies only: no users, permissions or passwords, which each install sets itself.
/// </summary>
public static class RabbitDefinitions
{
    /// <summary>
    /// The prefix of every dead-letter exchange and queue, as MessageBroker names them. Its first name part,
    /// <c>dead-letter</c>, is reserved: no topic or command queue may be named with it.
    /// </summary>
    public const string DeadLetterPrefix = RabbitSubscriber.DeadLetterPrefix;

    /// <summary>
    /// Writes the definitions.
    /// </summary>
    /// <param name="topology">The checked topology.</param>
    /// <param name="options">The settings, or the defaults.</param>
    /// <returns>The definitions, in a fixed order.</returns>
    /// <exception cref="BrokerTopologyException">
    /// Two declarations would make the same RabbitMQ object, a topic or command queue is named with the reserved
    /// <c>dead-letter</c> part, or a name is longer than RabbitMQ allows.
    /// </exception>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The virtual host, which RabbitMQ doesn't create when it imports definitions at startup.</item>
    /// <item>Each topic is a durable topic exchange. Each of its subscriptions is a durable quorum queue
    /// <c>{topic}.{subscription}</c>, bound with its family's wire name as routing key.</item>
    /// <item>Each command queue is a durable direct exchange and a durable quorum queue, both named <c>{queue}</c>, the
    /// queue bound with its own name as routing key. Senders publish to the queue's own exchange, so sending can be
    /// granted one queue at a time.</item>
    /// <item>Each subscription queue and command queue dead-letters to a fanout exchange and quorum queue of its own,
    /// <c>dead-letter.{queue}</c>, bound with an empty routing key.</item>
    /// <item>Each subscription queue and command queue has a policy of its own, matching its name exactly, with the
    /// delivery limit, its dead-letter exchange, <c>dead-letter-strategy: at-least-once</c> and
    /// <c>overflow: reject-publish</c>. Policies, not queue arguments, so an install can change them on a queue that
    /// exists.</item>
    /// </list>
    /// </remarks>
    public static JsonObject Build(BrokerTopology topology, RabbitDefinitionsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        options ??= new RabbitDefinitionsOptions();
        if (options.MaxDeliveryCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDeliveryCount must be at least 2: one delivery and one retry.");
        }

        var exchanges = new List<(string Name, string Type)>();
        var queues = new List<string>();
        var bindings = new List<(string Source, string Destination, string RoutingKey)>();
        // Each queue the limit and dead-lettering apply to, and where its dead letters go.
        var limited = new List<(string Queue, string DeadLetter)>();
        // Every name each declaration makes on the broker, which no other declaration may make too.
        var made = new List<(string Name, string Declaration)>();

        void DeadLettered(string queue, string declaration)
        {
            var deadLetter = DeadLetterPrefix + queue;
            queues.Add(queue);
            exchanges.Add((deadLetter, "fanout"));
            queues.Add(deadLetter);
            bindings.Add((deadLetter, deadLetter, ""));
            limited.Add((queue, deadLetter));
            made.Add((queue, declaration));
            made.Add((deadLetter, declaration));
        }

        foreach (var topic in topology.Topics)
        {
            exchanges.Add((topic.Name, "topic"));
            made.Add((topic.Name, $"the topic {topic.Name}"));
            foreach (var subscription in topic.Subscriptions)
            {
                var queue = RabbitSubscriber.QueueName(topic.Name, subscription.Name);
                DeadLettered(queue, $"the subscription {subscription.Name} on {topic.Name}");
                bindings.Add((topic.Name, queue, subscription.Family));
            }
        }

        foreach (var commandQueue in topology.CommandQueues)
        {
            var declaration = $"the command queue {commandQueue.Name}";
            exchanges.Add((commandQueue.Name, "direct"));
            made.Add((commandQueue.Name, declaration));
            DeadLettered(commandQueue.Name, declaration);
            bindings.Add((commandQueue.Name, commandQueue.Name, commandQueue.Name));
        }

        Check(topology, made);

        var vhost = options.VirtualHost;
        return new JsonObject
        {
            ["vhosts"] = new JsonArray(new JsonObject { ["name"] = vhost }),
            ["exchanges"] = new JsonArray([.. exchanges.OrderBy(e => e.Name, StringComparer.Ordinal).Select(e => (JsonNode)new JsonObject
            {
                ["name"] = e.Name,
                ["vhost"] = vhost,
                ["type"] = e.Type,
                ["durable"] = true,
                ["auto_delete"] = false,
                ["internal"] = false,
                ["arguments"] = new JsonObject(),
            })]),
            ["queues"] = new JsonArray([.. queues.Order(StringComparer.Ordinal).Select(q => (JsonNode)new JsonObject
            {
                ["name"] = q,
                ["vhost"] = vhost,
                ["durable"] = true,
                ["auto_delete"] = false,
                ["arguments"] = new JsonObject { ["x-queue-type"] = "quorum" },
            })]),
            ["bindings"] = new JsonArray([.. bindings
                .OrderBy(b => b.Source, StringComparer.Ordinal)
                .ThenBy(b => b.Destination, StringComparer.Ordinal)
                .ThenBy(b => b.RoutingKey, StringComparer.Ordinal)
                .Select(b => (JsonNode)new JsonObject
                {
                    ["source"] = b.Source,
                    ["vhost"] = vhost,
                    ["destination"] = b.Destination,
                    ["destination_type"] = "queue",
                    ["routing_key"] = b.RoutingKey,
                    ["arguments"] = new JsonObject(),
                })]),
            ["policies"] = new JsonArray([.. limited.OrderBy(l => l.Queue, StringComparer.Ordinal).Select(l => (JsonNode)new JsonObject
            {
                ["vhost"] = vhost,
                ["name"] = l.Queue,
                ["pattern"] = $"^{Regex.Escape(l.Queue)}$",
                ["apply-to"] = "queues",
                ["priority"] = 0,
                ["definition"] = new JsonObject
                {
                    // RabbitMQ counts redeliveries: the first delivery isn't one.
                    ["delivery-limit"] = options.MaxDeliveryCount - 1,
                    ["dead-letter-exchange"] = l.DeadLetter,
                    ["dead-letter-strategy"] = "at-least-once",
                    ["overflow"] = "reject-publish",
                },
            })]),
        };
    }

    private static void Check(BrokerTopology topology, List<(string Name, string Declaration)> made)
    {
        var problems = new List<string>();
        var reservedPart = DeadLetterPrefix.TrimEnd('.');
        var named = topology.Topics.Select(t => (t.Name, What: "topic"))
            .Concat(topology.CommandQueues.Select(q => (q.Name, What: "command queue")));
        foreach (var (name, what) in named.Where(d => (d.Name + ".").StartsWith(DeadLetterPrefix, StringComparison.Ordinal)))
        {
            problems.Add($"The {what} {name} is named with {reservedPart}, the first name part reserved for RabbitMQ's dead-letter exchanges and queues.");
        }

        // RabbitMQ grants rights by patterns over names, whatever the kind, so no two declarations may share a name.
        foreach (var name in made.GroupBy(m => m.Name, StringComparer.Ordinal))
        {
            var declarations = name.Select(m => m.Declaration).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (declarations.Count > 1)
            {
                problems.Add($"The RabbitMQ name {name.Key} would be made by {string.Join(" and ", declarations)}.");
            }

            if (Encoding.UTF8.GetByteCount(name.Key) > 255)
            {
                problems.Add($"The RabbitMQ name {name.Key} is longer than RabbitMQ's 255 bytes.");
            }
        }

        if (problems.Count > 0)
        {
            throw new BrokerTopologyException(problems);
        }
    }
}
