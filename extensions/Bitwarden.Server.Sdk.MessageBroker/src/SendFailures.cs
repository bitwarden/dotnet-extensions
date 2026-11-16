using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Which send failures retrying can never fix.
/// </summary>
internal static class SendFailures
{
    /// <summary>
    /// True for a failure that sending the same message again can't fix: today, a message too
    /// large for the broker. Anything else (an outage, a missing queue, credentials that expired,
    /// a permission not granted yet) may clear, so a sender that must never lose a message, such
    /// as an outbox, retries it.
    /// </summary>
    internal static bool IsPermanent(Exception exception) =>
        exception is ServiceBusException { Reason: ServiceBusFailureReason.MessageSizeExceeded };
}
