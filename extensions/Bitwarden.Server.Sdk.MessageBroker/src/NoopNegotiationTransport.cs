namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Noop implementation of <see cref="INegotiationTransport"/>
/// </summary>
internal sealed class NoopNegotiationTransport : INegotiationTransport
{
    private static NegotiationAck _nogo = new()
    {
        Go = false,
        Offenders = [new NegotiationIncompatibility
        {
            InstanceId = "noop",
            WireNames = new()
        }]
    };
    public IAsyncEnumerable<INegotiationRequest> ReceiveRequestsAsync(CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<INegotiationRequest>();
    public Task<NegotiationAck> SendCapabilityAsync(Capability capability, CancellationToken cancellationToken = default) => Task.FromResult(_nogo);
    public Task<NegotiationAck> SendJoinAsync(PublisherJoin join, CancellationToken cancellationToken = default) => Task.FromResult(_nogo);
}
