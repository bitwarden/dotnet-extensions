namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Fallback transport that satisfies <see cref="INegotiationTransport"/> when no distributed
/// backend is configured.
/// </summary>
internal sealed class NoopNegotiationTransport : INegotiationTransport
{
    private static readonly NegotiationAck _go = new() { Go = true };

    public IAsyncEnumerable<INegotiationInbound> ReceiveRequestsAsync(CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<INegotiationInbound>();
    public Task<NegotiationAck> SendCapabilityAsync(Capability capability, CancellationToken cancellationToken = default) => Task.FromResult(_go);
    public Task<NegotiationAck> SendJoinAsync(PublisherJoin join, CancellationToken cancellationToken = default) => Task.FromResult(_go);
    public Task SendPublisherLeaveAsync(PublisherLeave leave, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendSubscriberLeaveAsync(SubscriberLeave leave, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
