namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// A RabbitMQ delivery's 1-based delivery count, as <see cref="Envelope{TPayload, TCeiling}.DeliveryCount"/> has it.
/// </summary>
internal static class RabbitDeliveryCount
{
    /// <summary>
    /// Quorum queues count failed deliveries in <c>x-delivery-count</c> from 0, so the count is one more. RabbitMQ
    /// sends the header as a 64-bit integer and LavinMQ as a 32-bit one. Classic queues send none and only say
    /// whether the message was delivered before, so their count is 1 or 2.
    /// </summary>
    public static int Read(IDictionary<string, object?>? headers, bool redelivered)
    {
        if (headers?.TryGetValue("x-delivery-count", out var header) == true)
        {
            int? count = header switch
            {
                long value => (int)value,
                int value => value,
                short value => value,
                byte value => value,
                sbyte value => value,
                _ => null,
            };
            if (count is { } failed)
                return failed + 1;
        }

        return redelivered ? 2 : 1;
    }
}
