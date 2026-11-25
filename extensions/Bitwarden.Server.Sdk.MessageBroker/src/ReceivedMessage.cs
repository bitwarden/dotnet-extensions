namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A message received from a queue or a topic subscription, before it is decoded.
/// </summary>
internal sealed class ReceivedMessage
{
    /// <summary>
    /// The serialized variants, as the sender wrote them.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// The payload family wire name from the broker metadata, or null if it is missing.
    /// </summary>
    public required string? FamilyWireName { get; init; }

    /// <summary>
    /// The sender's message identifier.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// The W3C traceparent of the send span, if the message was sent under a trace.
    /// </summary>
    public required string? TraceParent { get; init; }

    /// <summary>
    /// One on the first delivery, incrementing with each redelivery.
    /// </summary>
    public required int DeliveryCount { get; init; }

    /// <summary>
    /// Acknowledges the message, removing it from the queue.
    /// </summary>
    public required Func<CancellationToken, Task> CompleteAsync { get; init; }

    /// <summary>
    /// Returns the message to the queue for redelivery.
    /// </summary>
    public required Func<CancellationToken, Task> RequeueAsync { get; init; }

    /// <summary>
    /// Removes the message without redelivery, with a reason where the broker records one.
    /// </summary>
    public required Func<string?, CancellationToken, Task> DeadLetterAsync { get; init; }
}
