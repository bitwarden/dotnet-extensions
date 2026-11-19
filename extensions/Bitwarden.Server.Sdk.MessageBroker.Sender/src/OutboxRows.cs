using System.Buffers;
using System.Diagnostics;

namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// Makes the outbox's row for a message, as every way of adding one does.
/// </summary>
internal static class OutboxRows
{
    /// <summary>
    /// Serializes <paramref name="variants"/> and makes the row that sends them to
    /// <paramref name="destination"/> under <paramref name="messageId"/>, no earlier than
    /// <paramref name="notBefore"/> if given.
    /// </summary>
    /// <exception cref="InvalidOperationException">The serialized message is larger than <paramref name="maxMessageSize"/>.</exception>
    public static OutboxMessage Create<TPayload>(
        IMessageSerializer serializer,
        string destination,
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        DateTimeOffset? notBefore,
        TimeProvider time,
        int maxMessageSize)
        where TPayload : Payload<TPayload>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
    {
        // Serialized now, while every version's data is at hand: an assisted downcast takes data
        // only the sending code has.
        var buffer = new ArrayBufferWriter<byte>();
        serializer.SerializeVariants<TPayload>(variants, buffer);
        // A local message never reaches a broker, so no broker's size limit applies.
        if (buffer.WrittenCount > maxMessageSize && destination != MessageOutbox.Local)
            throw new InvalidOperationException(
                $"A '{TPayload.WireName}' message to '{destination}' is {buffer.WrittenCount} bytes, more than the " +
                $"{maxMessageSize} the outbox accepts (MessageOutboxOptions.MaxMessageSize). Send identifiers, not contents.");
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var due = Math.Max(now, notBefore?.ToUnixTimeMilliseconds() ?? now);
        return new OutboxMessage
        {
            MessageId = messageId,
            Destination = destination,
            MessageType = TPayload.WireName,
            Body = buffer.WrittenSpan.ToArray(),
            TraceParent = Activity.Current?.Id,
            CreatedAt = now,
            DueAt = due,
            AvailableAt = due,
        };
    }
}
