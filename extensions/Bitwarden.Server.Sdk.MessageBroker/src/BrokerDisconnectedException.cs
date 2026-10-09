namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Thrown from <see cref="ISubscriber{TPayload, TCeiling}.SubscribeAsync"/> when the broker closes the connection
/// unexpectedly during an active subscription (not as a result of a cancellation request or normal
/// host shutdown). The original broker-specific exception is preserved as
/// <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class BrokerDisconnectedException : Exception
{
    /// <summary>The topic for which the subscription was active when the disconnect occurred.</summary>
    public string TopicName { get; }

    /// <summary>Initializes a new instance of <see cref="BrokerDisconnectedException"/>.</summary>
    public BrokerDisconnectedException(string topicName, Exception innerException)
        : base($"The message broker connection was unexpectedly closed for topic '{topicName}'.", innerException)
    {
        TopicName = topicName;
    }
}
