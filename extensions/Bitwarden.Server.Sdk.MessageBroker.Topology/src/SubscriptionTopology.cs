namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// A subscription on a topic: one family, read by one service.
/// </summary>
/// <param name="Name">The subscription's name.</param>
/// <param name="Family">The payload family it's bound or filtered to.</param>
/// <param name="Service">The service that reads it.</param>
public sealed record SubscriptionTopology(string Name, string Family, string Service);
