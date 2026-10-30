namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Thrown from concrete <see cref="Publisher{TPayload,TCeiling}"/> implementations when the message broker cannot
/// be reached to send a message. The original broker-specific exception is preserved as
/// <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class BrokerUnavailableException : Exception
{
    /// <summary>The topic for which the publish was attempted.</summary>
    public string TopicName { get; }

    /// <summary>Initializes a new instance of <see cref="BrokerUnavailableException"/>.</summary>
    public BrokerUnavailableException(string topicName, Exception innerException)
        : base($"The message broker is unavailable for topic '{topicName}'.", innerException)
    {
        TopicName = topicName;
    }
}
