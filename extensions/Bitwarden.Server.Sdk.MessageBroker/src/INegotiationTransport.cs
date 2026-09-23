namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Sends and receives control-plane messages for message-plane version negotiation.
/// <para>
/// <see cref="ReceiveRequestsAsync"/> is single-active-consumer across the service: at most one
/// process's enumeration yields a request at any moment. Implementations must uphold this
/// contract; callers may rely on it for serializing admission decisions.
/// </para>
/// <para>
/// Extends <see cref="IAsyncDisposable"/> because callers hold transports through the
/// interface and need to dispose broker connections without knowing the concrete type.
/// </para>
/// </summary>
internal interface INegotiationTransport : IAsyncDisposable
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
    /// Publishes a publisher's clean-shutdown leave and returns once the message has left the
    /// outbound pipe.
    /// </summary>
    Task SendPublisherLeaveAsync(
        PublisherLeave leave,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a subscriber's clean-shutdown leave. Same fire-and-forget semantics as
    /// <see cref="SendPublisherLeaveAsync"/>.
    /// </summary>
    Task SendSubscriberLeaveAsync(
        SubscriberLeave leave,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams inbound control-plane messages for the publisher-service subscription: admission
    /// requests (<see cref="CapabilityRequest"/>, <see cref="JoinRequest"/>) that must be replied
    /// to via <see cref="INegotiationRequest.ReplyAsync"/>, and fire-and-forget leave
    /// notifications (<see cref="PublisherLeaveNotification"/>,
    /// <see cref="SubscriberLeaveNotification"/>) that are processed and dropped.
    /// </summary>
    IAsyncEnumerable<INegotiationInbound> ReceiveRequestsAsync(
        CancellationToken cancellationToken = default);
}
