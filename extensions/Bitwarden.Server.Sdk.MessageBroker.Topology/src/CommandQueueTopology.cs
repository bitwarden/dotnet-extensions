namespace Bitwarden.Server.Sdk.MessageBroker.Topology;

/// <summary>
/// A command queue, the service that owns and reads it, the families it consumes, and who sends to it.
/// </summary>
/// <param name="Name">The queue's name.</param>
/// <param name="Owner">The service that reads it.</param>
/// <param name="Families">The command families its owner consumes.</param>
/// <param name="Senders">Each service and family sent to it.</param>
public sealed record CommandQueueTopology(string Name, string Owner, IReadOnlyList<string> Families, IReadOnlyList<CommandSenderTopology> Senders);
