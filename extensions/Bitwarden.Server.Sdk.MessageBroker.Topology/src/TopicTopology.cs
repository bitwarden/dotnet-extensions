namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// A topic, its owner, the families the owner publishes on it, and the subscriptions read from it.
/// </summary>
/// <param name="Name">The topic's name.</param>
/// <param name="Owner">The service that publishes to it.</param>
/// <param name="Families">The payload families published on it.</param>
/// <param name="Subscriptions">Its subscriptions.</param>
public sealed record TopicTopology(string Name, string Owner, IReadOnlyList<string> Families, IReadOnlyList<SubscriptionTopology> Subscriptions);
