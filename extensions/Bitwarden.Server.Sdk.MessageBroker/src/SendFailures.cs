using Azure.Messaging.ServiceBus;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Which send failures retrying can never fix.
/// </summary>
internal static class SendFailures
{
    /// <summary>
    /// True for a failure that sending the same message again can't fix, such as a message too
    /// large for the broker.
    /// </summary>
    internal static bool IsPermanent(Exception exception) =>
        exception is ServiceBusException { Reason: ServiceBusFailureReason.MessageSizeExceeded };
}
