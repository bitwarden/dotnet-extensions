namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// An incoming negotiation request to be answered. Discriminate with pattern matching between
/// <see cref="CapabilityRequest"/> and <see cref="JoinRequest"/> to determine which side of the
/// admission check to run.
/// </summary>
internal interface INegotiationRequest
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
