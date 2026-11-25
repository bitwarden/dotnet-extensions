namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Decodes a <see cref="ReceivedMessage"/> into an envelope for one payload family, for topic
/// subscriptions and command queues alike, and dead-letters what it can't decode.
/// </summary>
internal static class ReceivedMessageDecoder
{
    /// <summary>
    /// Returns the envelope, or null once the message has been dead-lettered with the reason it
    /// could not be decoded. A returned envelope has started its consumer span and been counted.
    /// </summary>
    /// <param name="message">The message as received.</param>
    /// <param name="serializer">The serializer for <typeparamref name="TPayload"/>'s family.</param>
    /// <param name="entityName">The topic or queue, for the consumer span and the consume metric.</param>
    /// <param name="metrics">The message broker metrics.</param>
    public static async Task<Envelope<TPayload, TCeiling>?> DecodeOrDeadLetterAsync<TPayload, TCeiling>(
        ReceivedMessage message, IMessageSerializer serializer, string entityName, MessageBrokerMetrics metrics)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>, IPayloadWireName<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
    {
        IReadOnlyList<Payload<TPayload>.IVariant> variants;
        try
        {
            variants = serializer.DeserializeVariants<TPayload>(message.Body.Span);
        }
        catch (Exception)
        {
            await DeadLetterAsync(message, "The message's body could not be deserialized.");
            return null;
        }

        if (variants.Count == 0)
        {
            await DeadLetterAsync(message, "No version of the message on the wire is known to this receiver.");
            return null;
        }

        var activity = MessageBrokerActivitySource.StartConsumerActivity(entityName, TPayload.WireName, message.TraceParent);
        ReceivedEnvelope<TPayload, TCeiling> envelope;
        try
        {
            envelope = new ReceivedEnvelope<TPayload, TCeiling>(variants, message, activity);
        }
        catch (InvalidOperationException)
        {
            activity?.Dispose();
            await DeadLetterAsync(message, $"The message's versions cannot be resolved to {typeof(TCeiling).Name}.");
            return null;
        }

        metrics.RecordConsume(entityName, TPayload.WireName, envelope.ConsumedVariantWireName);
        return envelope;
    }

    /// <summary>
    /// Dead-letters <paramref name="message"/>, leaving it for redelivery if the broker refuses.
    /// </summary>
    public static async Task DeadLetterAsync(ReceivedMessage message, string reason)
    {
        try
        {
            await message.DeadLetterAsync(reason, CancellationToken.None);
        }
        catch (Exception)
        {
            // An unsettled message is redelivered when its lock expires.
        }
    }
}
