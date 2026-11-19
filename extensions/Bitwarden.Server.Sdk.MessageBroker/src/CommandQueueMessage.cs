namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A message received from a command queue before its payload family is known.
/// </summary>
internal sealed class CommandQueueMessage
{
    /// <summary>The serialized variants, as the sender wrote them.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>The payload family wire name from the broker metadata, or null if it is missing.</summary>
    public required string? FamilyWireName { get; init; }

    public required string MessageId { get; init; }

    /// <summary>The W3C traceparent of the send span, if the command was sent under a trace.</summary>
    public required string? TraceParent { get; init; }

    /// <summary>One on the first delivery, incrementing with each redelivery.</summary>
    public required int DeliveryCount { get; init; }

    /// <summary>Acknowledges the message, removing it from the queue.</summary>
    public required Func<CancellationToken, Task> CompleteAsync { get; init; }

    /// <summary>Returns the message to the queue for redelivery.</summary>
    public required Func<CancellationToken, Task> RequeueAsync { get; init; }

    /// <summary>Removes the message without redelivery, with a reason where the broker records one.</summary>
    public required Func<string?, CancellationToken, Task> DeadLetterAsync { get; init; }
}

/// <summary>Receives every message on one command queue, whatever its payload family.</summary>
internal interface ICommandQueueReceiver
{
    /// <summary>
    /// Returns the queue's messages as they arrive, one unsettled message at a time, calling
    /// <paramref name="onSubscribed"/> once the receiver is in place, whether or not a message comes.
    /// </summary>
    IAsyncEnumerable<CommandQueueMessage> ReceiveAsync(Action onSubscribed, CancellationToken cancellationToken);
}
