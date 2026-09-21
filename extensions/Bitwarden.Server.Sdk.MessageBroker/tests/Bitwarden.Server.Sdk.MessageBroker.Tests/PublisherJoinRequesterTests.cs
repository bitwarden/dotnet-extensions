using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class PublisherJoinRequesterTests
{
    private const string Topic = "topic";
    private static readonly NegotiationOptions StandardOptions = new()
    {
        ServiceName = "svc",
        ProcessDisplayName = "test",
        AdmissionTimeout = TimeSpan.FromSeconds(2),
    };

    [Fact]
    public async Task JoinRequestSucceedsWhenListenerRepliesGo()
    {
        await using var harness = StartListener(reply: _ => new NegotiationAck { Go = true });
        var marker = new PublisherRoleMarker(Topic, new HashSet<string> { "v1" });

        await PublisherJoinRequester.RequestJoinAsync(
            marker, harness.Sender, StandardOptions, TestContext.Current.CancellationToken);

        // No exception thrown = fleet admitted the requester
    }

    [Fact]
    public async Task JoinRequestThrowsWhenListenerRepliesNoGo()
    {
        await using var harness = StartListener(reply: _ => new NegotiationAck
        {
            Go = false,
            Offenders = [new NegotiationIncompatibility { InstanceId = "downstream-blocker", WireNames = new HashSet<string> { "v99" } }],
        });
        var marker = new PublisherRoleMarker(Topic, new HashSet<string> { "v1" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PublisherJoinRequester.RequestJoinAsync(
                marker, harness.Sender, StandardOptions, TestContext.Current.CancellationToken));

        Assert.Contains("downstream-blocker", ex.Message);
    }

    [Fact]
    public async Task JoinRequestThrowsOnTimeoutWhenNoListenerReplies()
    {
        // No listener attached — the send times out at AdmissionTimeout (2s) with no ack.
        var broker = new InMemoryNegotiationBroker();
        var sender = new InMemoryNegotiationTransport(broker, dataTopic: "sender-ignored");
        var marker = new PublisherRoleMarker(Topic, new HashSet<string> { "v1" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PublisherJoinRequester.RequestJoinAsync(
                marker, sender, StandardOptions, TestContext.Current.CancellationToken));

        Assert.Contains("timed out", ex.Message);
    }

    private static ListenerHarness StartListener(Func<PublisherJoin, NegotiationAck> reply)
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
                    if (request is JoinRequest jr)
                        await jr.ReplyAsync(reply(jr.Join), cts.Token);
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
