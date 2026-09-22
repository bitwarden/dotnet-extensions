namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Thrown at host startup when a publisher's <see cref="PublisherJoin"/> or a subscriber's
/// <see cref="Capability"/> receives no reply within <see cref="NegotiationOptions.AdmissionTimeout"/>.
/// A subscriber registration with <c>proceedOnAdmissionTimeout</c> set softens this into a
/// logged error and lets startup continue; otherwise the host fails to start.
/// </summary>
public sealed class NegotiationTimeoutException : Exception
{
    /// <summary>The data-topic whose admission request timed out.</summary>
    public string DataTopic { get; }

    /// <summary>The window that elapsed without a reply.</summary>
    public TimeSpan AdmissionTimeout { get; }

    /// <summary>Initializes a new instance of <see cref="NegotiationTimeoutException"/>.</summary>
    public NegotiationTimeoutException(string dataTopic, TimeSpan admissionTimeout)
        : base($"Negotiation admission for data-topic '{dataTopic}' timed out after " +
               $"{admissionTimeout} without a reply.")
    {
        DataTopic = dataTopic;
        AdmissionTimeout = admissionTimeout;
    }
}
