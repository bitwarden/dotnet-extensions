namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Shared behavior tests for <see cref="INegotiationTransport"/> implementations. Backend
/// fixtures derive from this and supply <see cref="CreateTransportAsync"/>. Backends that
/// carry state between tests should override <see cref="InitializeAsync"/> to drain that state.
/// </summary>
public abstract class NegotiationTransportBehaviorTests : IAsyncLifetime
{
    /// <summary>
    /// Service name used by these tests. Backend fixtures must pre-provision
    /// <c>request-{ServiceName}</c> and <c>reply-{ServiceName}</c> subscriptions.
    /// </summary>
    protected virtual string ServiceName => "negotiation";

    /// <summary>Builds a transport instance with the given operator label.</summary>
    internal abstract Task<INegotiationTransport> CreateTransportAsync(string processDisplayName);

    /// <summary>
    /// Per-test setup hook. Backends that carry state between tests should override to drain
    /// that state; the default is a no-op.
    /// </summary>
    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact(Timeout = 60 * 1000)]
    public async Task SendCapabilityYieldsCapabilityRequestAndReturnsReply()
    {
        await using var subscriber = (IAsyncDisposable)await CreateTransportAsync("subscriber");
        await using var publisher = (IAsyncDisposable)await CreateTransportAsync("publisher");

        var subscriberTransport = (INegotiationTransport)subscriber;
        var publisherTransport = (INegotiationTransport)publisher;

        using var publisherCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        INegotiationRequest? received = null;
        var ready = new TaskCompletionSource();
        var publisherTask = Task.Run(async () =>
        {
            ready.TrySetResult();
            await foreach (var request in publisherTransport.ReceiveRequestsAsync(publisherCts.Token))
            {
                received = request;
                await request.ReplyAsync(new NegotiationAck { Go = true }, publisherCts.Token);
                break;
            }
        }, TestContext.Current.CancellationToken);
        await ready.Task;

        var ack = await subscriberTransport.SendCapabilityAsync(
            new Capability { DataTopic = "topic", InstanceId = "sub-1", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);

        Assert.True(ack.Go);
        Assert.Empty(ack.Offenders);
        await publisherTask;
        var capabilityRequest = Assert.IsType<CapabilityRequest>(received);
        Assert.Equal("sub-1", capabilityRequest.Capability.InstanceId);

        await publisherCts.CancelAsync();
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task SendJoin_receiverGetsJoinRequestAndSenderGetsAckWithOffenders()
    {
        await using var bootingPublisher = (IAsyncDisposable)await CreateTransportAsync("booting");
        await using var activePublisher = (IAsyncDisposable)await CreateTransportAsync("active");

        var bootingTransport = (INegotiationTransport)bootingPublisher;
        var activeTransport = (INegotiationTransport)activePublisher;

        using var activeCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        INegotiationRequest? received = null;
        var ready = new TaskCompletionSource();
        var activeTask = Task.Run(async () =>
        {
            ready.TrySetResult();
            await foreach (var request in activeTransport.ReceiveRequestsAsync(activeCts.Token))
            {
                received = request;
                await request.ReplyAsync(
                    new NegotiationAck
                    {
                        Go = false,
                        Offenders =
                        [
                            new NegotiationIncompatibility
                            {
                                InstanceId = "sub-blocking",
                                WireNames = ["v99"],
                            },
                        ],
                    },
                    activeCts.Token);
                break;
            }
        }, TestContext.Current.CancellationToken);
        await ready.Task;

        var ack = await bootingTransport.SendJoinAsync(
            new PublisherJoin { DataTopic = "topic", InstanceId = "pub-1", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);

        Assert.False(ack.Go);
        var offender = Assert.Single(ack.Offenders);
        Assert.Equal("sub-blocking", offender.InstanceId);
        Assert.Contains("v99", offender.WireNames);
        await activeTask;
        var joinRequest = Assert.IsType<JoinRequest>(received);
        Assert.Equal("pub-1", joinRequest.Join.InstanceId);

        await activeCts.CancelAsync();
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task ConcurrentSubscribers_eachReceiveTheirOwnReply()
    {
        // Two subscribers send concurrently. The publisher echoes each requester's InstanceId
        // back in the ack. Session-id routing on the reply subscription must deliver each ack
        // to its own subscriber.
        await using var subscriberA = (IAsyncDisposable)await CreateTransportAsync("subscriberA");
        await using var subscriberB = (IAsyncDisposable)await CreateTransportAsync("subscriberB");
        await using var publisher = (IAsyncDisposable)await CreateTransportAsync("publisher");

        var subATransport = (INegotiationTransport)subscriberA;
        var subBTransport = (INegotiationTransport)subscriberB;
        var publisherTransport = (INegotiationTransport)publisher;

        using var publisherCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var ready = new TaskCompletionSource();
        var publisherTask = Task.Run(async () =>
        {
            ready.TrySetResult();
            var handled = 0;
            await foreach (var request in publisherTransport.ReceiveRequestsAsync(publisherCts.Token))
            {
                var incoming = ((CapabilityRequest)request).Capability;
                await request.ReplyAsync(
                    new NegotiationAck
                    {
                        Go = false,
                        Offenders =
                        [
                            new NegotiationIncompatibility
                            {
                                InstanceId = incoming.InstanceId,
                                WireNames = ["echo"],
                            },
                        ],
                    },
                    publisherCts.Token);
                if (++handled >= 2) break;
            }
        }, TestContext.Current.CancellationToken);
        await ready.Task;

        var ackATask = subATransport.SendCapabilityAsync(
            new Capability { DataTopic = "topic", InstanceId = "sub-A", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);
        var ackBTask = subBTransport.SendCapabilityAsync(
            new Capability { DataTopic = "topic", InstanceId = "sub-B", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);

        var ackA = await ackATask;
        var ackB = await ackBTask;

        Assert.Equal("sub-A", Assert.Single(ackA.Offenders).InstanceId);
        Assert.Equal("sub-B", Assert.Single(ackB.Offenders).InstanceId);

        await publisherTask;
        await publisherCts.CancelAsync();
    }
}
