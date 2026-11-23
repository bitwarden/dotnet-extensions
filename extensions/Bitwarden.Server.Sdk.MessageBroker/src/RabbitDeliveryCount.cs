namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A RabbitMQ delivery's 1-based delivery count, as <see cref="Envelope{TPayload, TCeiling}.DeliveryCount"/> has it.
/// </summary>
internal static class RabbitDeliveryCount
{
    /// <summary>
    /// Quorum queues count failed deliveries in <c>x-delivery-count</c> from 0, as a 64-bit integer, so the count
    /// is one more. Classic queues send none and only say whether the message was delivered before, so their count
    /// is 1 or 2.
    /// </summary>
    public static int Read(IDictionary<string, object?>? headers, bool redelivered)
    {
        if (headers?.TryGetValue("x-delivery-count", out var header) == true && header is long failed)
        {
            return (int)failed + 1;
        }

        return redelivered ? 2 : 1;
    }
}
