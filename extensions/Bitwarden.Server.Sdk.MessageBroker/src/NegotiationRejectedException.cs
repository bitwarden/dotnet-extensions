namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Thrown at host startup when the fleet's active consumer replies to a
/// no-go <see cref="PublisherJoin"/> or <see cref="Capability"/>. The
/// host fails to start. Unlike <see cref="NegotiationTimeoutException"/>, this outcome is
/// never softened by <c>proceedOnAdmissionTimeout</c>: a real wire-name incompatibility is
/// always a deploy-time gate failure.
/// </summary>
public sealed class NegotiationRejectedException : Exception
{
    /// <summary>The data-topic whose admission was rejected.</summary>
    public string DataTopic { get; }

    /// <summary>
    /// Instance identifiers of the counterparties whose wire-name sets do not overlap the
    /// requester's. For a rejected publisher these are subscribers; for a rejected subscriber
    /// these are publishers.
    /// </summary>
    public IReadOnlyList<string> OffenderInstanceIds { get; }

    /// <summary>Initializes a new instance of <see cref="NegotiationRejectedException"/>.</summary>
    public NegotiationRejectedException(string dataTopic, IReadOnlyList<string> offenderInstanceIds)
        : base($"Negotiation admission for data-topic '{dataTopic}' was rejected by the fleet " +
               $"(incompatible counterparties: {string.Join(", ", offenderInstanceIds)}).")
    {
        DataTopic = dataTopic;
        OffenderInstanceIds = offenderInstanceIds;
    }
}
