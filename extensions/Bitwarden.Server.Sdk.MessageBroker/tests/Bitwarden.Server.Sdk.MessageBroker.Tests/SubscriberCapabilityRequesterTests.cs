using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class SubscriberCapabilityRequesterTests
{
    private const string Topic = "topic";
    private static readonly NegotiationOptions StandardOptions = new()
    {
        ServiceName = "svc",
        ProcessDisplayName = "test",
        AdmissionTimeout = TimeSpan.FromSeconds(2),
    };

    [Fact]
    public async Task CapabilityRequestSucceedsWhenListenerRepliesGo()
    {
        await using var harness = StartListener(reply: _ => new NegotiationAck { Go = true });
        var marker = new SubscriberRoleMarker(Topic, new HashSet<string> { "v1" });

        await SubscriberJoinRequester.RequestCapabilityAsync(
            marker, harness.Sender, StandardOptions, TestContext.Current.CancellationToken);

        // No exception thrown = fleet admitted the subscriber
    }

    [Fact]
    public async Task CapabilityRequestThrowsWhenListenerRepliesNoGo()
    {
        await using var harness = StartListener(reply: _ => new NegotiationAck
        {
            Go = false,
            Offenders = [new NegotiationIncompatibility { InstanceId = "incompatible-pub", WireNames = new HashSet<string> { "v99" } }],
        });
        var marker = new SubscriberRoleMarker(Topic, new HashSet<string> { "v1" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubscriberJoinRequester.RequestCapabilityAsync(
                marker, harness.Sender, StandardOptions, TestContext.Current.CancellationToken));

        Assert.Contains("incompatible-pub", ex.Message);
    }

    [Fact]
    public async Task CapabilityRequestThrowsOnTimeoutWhenNoListenerReplies()
    {
        // No listener attached — the send times out at AdmissionTimeout (2s) with no ack.
        var broker = new InMemoryNegotiationBroker();
        var sender = new InMemoryNegotiationTransport(broker, dataTopic: "sender-ignored");
        var marker = new SubscriberRoleMarker(Topic, new HashSet<string> { "v1" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubscriberJoinRequester.RequestCapabilityAsync(
                marker, sender, StandardOptions, TestContext.Current.CancellationToken));

        Assert.Contains("timed out", ex.Message);
    }

    private static ListenerHarness StartListener(Func<Capability, NegotiationAck> reply)
    {
        var broker = new InMemoryNegotiationBroker();
        var listenerTransport = new InMemoryNegotiationTransport(broker, dataTopic: Topic);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var request in listenerTransport.ReceiveRequestsAsync(cts.Token))
                {
                    if (request is CapabilityRequest cr)
                        await cr.ReplyAsync(reply(cr.Capability), cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });

        var sender = new InMemoryNegotiationTransport(broker, dataTopic: "sender-ignored");
        return new ListenerHarness(sender, cts, runTask);
    }

    private sealed class ListenerHarness(
        INegotiationTransport sender,
        CancellationTokenSource cts,
        Task runTask) : IAsyncDisposable
    {
        public INegotiationTransport Sender { get; } = sender;

        public async ValueTask DisposeAsync()
        {
            await cts.CancelAsync();
            try { await runTask; } catch (OperationCanceledException) { }
            cts.Dispose();
        }
    }
}
