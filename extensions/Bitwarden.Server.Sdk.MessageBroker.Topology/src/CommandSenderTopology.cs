namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// A service that sends one command family to a queue.
/// </summary>
/// <param name="Service">The sending service.</param>
/// <param name="Family">The command family it sends.</param>
public sealed record CommandSenderTopology(string Service, string Family);
