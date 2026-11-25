namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Handles one command family on one command queue: decodes a <see cref="CommandQueueMessage"/>
/// into an <see cref="Envelope{TPayload, TCeiling}"/> and runs the registered consumer.
/// </summary>
internal interface ICommandFamilyHandler
{
    /// <summary>
    /// The command queue this handler receives from.
    /// </summary>
    string QueueName { get; }

    /// <summary>
    /// The wire name of the payload family this handler decodes.
    /// </summary>
    string FamilyWireName { get; }

    Task HandleAsync(CommandQueueMessage message, CancellationToken stoppingToken);
}
