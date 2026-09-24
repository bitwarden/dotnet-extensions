namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Fallback sender that satisfies <see cref="INegotiationSender"/> when no distributed backend
/// is configured. Every admission call replies Go; every leave is a no-op.
/// </summary>
internal sealed class NoopNegotiationTransport : INegotiationSender
{
    private static readonly NegotiationAck _go = new() { Go = true };

    public Task<NegotiationAck> SendCapabilityAsync(Capability capability, CancellationToken cancellationToken = default) => Task.FromResult(_go);
    public Task<NegotiationAck> SendJoinAsync(PublisherJoin join, CancellationToken cancellationToken = default) => Task.FromResult(_go);
    public Task SendPublisherLeaveAsync(PublisherLeave leave, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendSubscriberLeaveAsync(SubscriberLeave leave, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
