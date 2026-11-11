namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Umbrella marker for anything the transport yields off the control plane. Two shapes:
/// <see cref="INegotiationRequest"/> for admission traffic (subscriber capability, publisher
/// join) that must be answered with a <see cref="NegotiationAck"/>; and the fire-and-forget
/// notification records (<see cref="PublisherLeaveNotification"/>,
/// <see cref="SubscriberLeaveNotification"/>) that the listener processes and drops without
/// replying. Discriminate with pattern matching to decide which side to run.
/// </summary>
internal interface INegotiationInbound { }

/// <summary>
/// An admission request that expects a go/no-go reply. Only the two admission types
/// (<see cref="CapabilityRequest"/>, <see cref="JoinRequest"/>) implement this; leave
/// notifications deliberately do not, because there is nothing the sender is waiting on.
/// </summary>
internal interface INegotiationRequest : INegotiationInbound
{
    /// <summary>Sends the go/no-go reply back to the requester.</summary>
    Task ReplyAsync(NegotiationAck ack, CancellationToken cancellationToken = default);
}

/// <summary>A subscriber's <see cref="Capability"/> arriving on the control plane.</summary>
internal sealed record CapabilityRequest : INegotiationRequest
{
    public required Capability Capability { get; init; }

    internal required Func<NegotiationAck, CancellationToken, Task> Replier { get; init; }

    public Task ReplyAsync(NegotiationAck ack, CancellationToken cancellationToken = default)
        => Replier(ack, cancellationToken);
}

/// <summary>A booting publisher's <see cref="PublisherJoin"/> arriving on the control plane.</summary>
internal sealed record JoinRequest : INegotiationRequest
{
    public required PublisherJoin Join { get; init; }

    internal required Func<NegotiationAck, CancellationToken, Task> Replier { get; init; }

    public Task ReplyAsync(NegotiationAck ack, CancellationToken cancellationToken = default)
        => Replier(ack, cancellationToken);
}

/// <summary>
/// A publisher's <see cref="PublisherLeave"/> arriving on the control plane. Fire-and-forget:
/// no replier because the sender never waits for an ack.
/// </summary>
internal sealed record PublisherLeaveNotification : INegotiationInbound
{
    public required PublisherLeave Leave { get; init; }
}

/// <summary>
/// A subscriber's <see cref="SubscriberLeave"/> arriving on the control plane. Fire-and-forget:
/// no replier because the sender never waits for an ack.
/// </summary>
internal sealed record SubscriberLeaveNotification : INegotiationInbound
{
    public required SubscriberLeave Leave { get; init; }
}
