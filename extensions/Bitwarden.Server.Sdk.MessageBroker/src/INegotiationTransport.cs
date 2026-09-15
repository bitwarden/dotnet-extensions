namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Sends and receives control-plane messages for message-plane version negotiation.
/// <para>
/// <see cref="ReceiveRequestsAsync"/> is single-active-consumer across the service: at most one
/// process's enumeration yields a request at any moment. Implementations must uphold this
/// contract; callers may rely on it for serializing admission decisions.
/// </para>
/// </summary>
internal interface INegotiationTransport
{
    /// <summary>Sends a subscriber's capability and awaits the go/no-go reply.</summary>
    Task<NegotiationAck> SendCapabilityAsync(
        Capability capability,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a booting publisher's join and awaits the go/no-go reply.</summary>
    Task<NegotiationAck> SendJoinAsync(
        PublisherJoin join,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams incoming control-plane requests for the publisher-service subscription. Each
    /// request must be replied to via <see cref="INegotiationRequest.ReplyAsync"/>.
    /// </summary>
    IAsyncEnumerable<INegotiationRequest> ReceiveRequestsAsync(
        CancellationToken cancellationToken = default);
}
