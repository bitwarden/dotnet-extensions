using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// A topology as Azure Service Bus entities (BROKER-TOPOLOGY.md, "Azure Service Bus"): a topic per topic, a
/// subscription per subscription with a correlation filter on its family, a queue per command queue, and each service's
/// rights. Written as the parameters of <c>infra/messaging/message-broker.bicep</c>, and as the Service Bus emulator's
/// configuration, from the same entities.
/// </summary>
public static partial class ServiceBusTopology
{
    /// <summary>
    /// The application property publishers stamp each message's payload family in, which subscriptions filter on.
    /// </summary>
    public const string FamilyProperty = "x-bw-payload-family";

    /// <summary>
    /// The emulator's one namespace, which its connection string doesn't name.
    /// </summary>
    public const string EmulatorNamespace = "sbemulatorns";

    /// <summary>
    /// Writes the parameters of <c>infra/messaging/message-broker.bicep</c> that come from the services: the topics,
    /// subscriptions, command queues and senders, each with the service it belongs to. The namespace and each service's
    /// identity differ by environment, so the deployment gives those.
    /// </summary>
    /// <param name="topology">The checked topology.</param>
    /// <param name="options">The settings, or the defaults.</param>
    /// <returns>An ARM deployment parameters file.</returns>
    /// <exception cref="BrokerTopologyException">A name isn't one Service Bus allows, or a topic and a queue share one.</exception>
    public static JsonObject BicepParameters(BrokerTopology topology, ServiceBusOptions? options = null)
    {
        options = Checked(topology, options);
        return new JsonObject
        {
            ["$schema"] = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
            ["contentVersion"] = "1.0.0.0",
            ["parameters"] = new JsonObject
            {
                ["maxDeliveryCount"] = Value(options.MaxDeliveryCount),
                ["topics"] = Value(new JsonArray([.. topology.Topics.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name,
                    ["owner"] = t.Owner,
                })])),
                ["subscriptions"] = Value(new JsonArray([.. topology.Topics.SelectMany(t => t.Subscriptions.Select(s => (JsonNode)new JsonObject
                {
                    ["topic"] = t.Name,
                    ["name"] = s.Name,
                    ["family"] = s.Family,
                    ["reader"] = s.Service,
                }))])),
                ["commandQueues"] = Value(new JsonArray([.. topology.CommandQueues.Select(q => (JsonNode)new JsonObject
                {
                    ["name"] = q.Name,
                    ["owner"] = q.Owner,
                })])),
                // One right per service and queue, however many families it sends there.
                ["commandSenders"] = Value(new JsonArray([.. topology.CommandQueues.SelectMany(q => q.Senders
                    .Select(s => s.Service).Distinct(StringComparer.Ordinal)
                    .Select(service => (JsonNode)new JsonObject { ["queue"] = q.Name, ["service"] = service }))])),
            },
        };
    }

    /// <summary>
    /// Writes the Service Bus emulator's configuration (<c>/ServiceBus_Emulator/ConfigFiles/Config.json</c>): the same
    /// entities as <see cref="BicepParameters"/>, in the emulator's one namespace, for tests and local runs. The
    /// emulator has no rights to grant.
    /// </summary>
    /// <param name="topology">The checked topology.</param>
    /// <param name="options">The settings, or the defaults.</param>
    /// <returns>The emulator's configuration.</returns>
    /// <exception cref="BrokerTopologyException">A name isn't one Service Bus allows, or a topic and a queue share one.</exception>
    public static JsonObject EmulatorConfig(BrokerTopology topology, ServiceBusOptions? options = null)
    {
        options = Checked(topology, options);
        return new JsonObject
        {
            ["UserConfig"] = new JsonObject
            {
                ["Namespaces"] = new JsonArray(new JsonObject
                {
                    ["Name"] = EmulatorNamespace,
                    ["Queues"] = new JsonArray([.. topology.CommandQueues.Select(q => (JsonNode)new JsonObject
                    {
                        ["Name"] = q.Name,
                        ["Properties"] = new JsonObject
                        {
                            ["DeadLetteringOnMessageExpiration"] = true,
                            ["LockDuration"] = "PT1M",
                            ["MaxDeliveryCount"] = options.MaxDeliveryCount,
                            ["RequiresDuplicateDetection"] = true,
                            ["DuplicateDetectionHistoryTimeWindow"] = EmulatorDuplicateWindow,
                            ["RequiresSession"] = false,
                        },
                    })]),
                    ["Topics"] = new JsonArray([.. topology.Topics.Select(t => (JsonNode)new JsonObject
                    {
                        ["Name"] = t.Name,
                        ["Properties"] = new JsonObject
                        {
                            ["RequiresDuplicateDetection"] = true,
                            ["DuplicateDetectionHistoryTimeWindow"] = EmulatorDuplicateWindow,
                        },
                        ["Subscriptions"] = new JsonArray([.. t.Subscriptions.Select(s => (JsonNode)new JsonObject
                        {
                            ["Name"] = s.Name,
                            ["Properties"] = new JsonObject
                            {
                                ["DeadLetteringOnMessageExpiration"] = true,
                                ["LockDuration"] = "PT1M",
                                ["MaxDeliveryCount"] = options.MaxDeliveryCount,
                                ["RequiresSession"] = false,
                            },
                            ["Rules"] = new JsonArray(new JsonObject
                            {
                                ["Name"] = "family",
                                ["Properties"] = new JsonObject
                                {
                                    ["FilterType"] = "Correlation",
                                    ["CorrelationFilter"] = new JsonObject
                                    {
                                        ["Properties"] = new JsonObject { [FamilyProperty] = s.Family },
                                    },
                                },
                            }),
                        })]),
                    })]),
                }),
                ["Logging"] = new JsonObject { ["Type"] = "File" },
            },
        };
    }

    // The template's ten minutes, cut to the five the emulator allows.
    private const string EmulatorDuplicateWindow = "PT5M";

    private static JsonObject Value(JsonNode value) => new() { ["value"] = value };

    private static ServiceBusOptions Checked(BrokerTopology topology, ServiceBusOptions? options)
    {
        ArgumentNullException.ThrowIfNull(topology);
        options ??= new ServiceBusOptions();
        if (options.MaxDeliveryCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxDeliveryCount must be at least 1.");
        }

        var problems = new List<string>();
        var entities = topology.Topics.Select(t => (Name: t.Name, What: "topic"))
            .Concat(topology.CommandQueues.Select(q => (Name: q.Name, What: "queue")))
            .ToList();
        foreach (var (name, what) in entities)
        {
            // Topics and queues: letters, digits, periods, hyphens and underscores, starting and ending with a letter or
            // digit, up to 260 characters. Slashes would make a path; none of ours needs one.
            if (name.Length > 260 || !EntityName().IsMatch(name))
            {
                problems.Add($"The {what} {name} isn't a Service Bus {what} name: letters, digits, '.', '-' and '_', starting and ending with a letter or digit, up to 260 characters.");
            }
        }

        foreach (var same in entities.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            // A namespace's topics and queues share one set of names, without regard to case.
            problems.Add($"{string.Join(", ", same.Select(e => $"the {e.What} {e.Name}"))} would be one Service Bus entity: topics and queues share one set of names, without regard to case.");
        }

        foreach (var topic in topology.Topics)
        {
            foreach (var same in topic.Subscriptions.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                problems.Add($"The subscriptions {string.Join(" and ", same.Select(s => s.Name))} on {topic.Name} would be one Service Bus subscription, without regard to case.");
            }

            foreach (var subscription in topic.Subscriptions.Where(s => s.Name.Length > 50 || !EntityName().IsMatch(s.Name)))
            {
                problems.Add($"The subscription {subscription.Name} on {topic.Name} isn't a Service Bus subscription name: letters, digits, '.', '-' and '_', starting and ending with a letter or digit, up to 50 characters.");
            }
        }

        if (problems.Count > 0)
        {
            throw new BrokerTopologyException(problems);
        }

        return options;
    }

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$")]
    private static partial Regex EntityName();
}
