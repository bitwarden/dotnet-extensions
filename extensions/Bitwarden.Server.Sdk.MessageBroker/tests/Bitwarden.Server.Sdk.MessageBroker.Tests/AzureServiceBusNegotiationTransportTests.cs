using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class AzureServiceBusNegotiationTransportTests
    : NegotiationTransportBehaviorTests, IClassFixture<AzureServiceBusFixture>
{
    private const string ShortLockServiceName = "shortlock";
    private const string ControlTopic = "ctrl";
    private const string DataTopic = "topic";

    private readonly AzureServiceBusFixture _fixture;

    public AzureServiceBusNegotiationTransportTests(AzureServiceBusFixture fixture)
    {
        _fixture = fixture;
    }

    internal override Task<INegotiationTransport> CreateTransportAsync(string processDisplayName)
        => Task.FromResult(CreateTransport(processDisplayName));

    // Session-enabled subscriptions on the shared emulator carry state between sequential
    // tests. Drain all scopes this class touches — request/reply sessions plus the request
    // subscription's dead-letter queue (dead-letter tests would otherwise see prior tests'
    // residue) — in parallel; every test pays this on entry.
    public override async ValueTask InitializeAsync() =>
        await Task.WhenAll(
            _fixture.DrainSessionsAsync(ControlTopic, "request-" + ServiceName),
            _fixture.DrainSessionsAsync(ControlTopic, "reply-" + ServiceName),
            _fixture.DrainSessionsAsync(ControlTopic, "request-" + ShortLockServiceName),
            _fixture.DrainSessionsAsync(ControlTopic, "reply-" + ShortLockServiceName),
            _fixture.DrainSubscriptionAsync(ControlTopic, "request-" + ServiceName, SubQueue.DeadLetter));

    [Fact(Timeout = 90 * 1000)]
    public async Task ReplyAfterTimeoutLoopStaysAlive()
    {
        await using var subscriber = (IAsyncDisposable)CreateTransport("subscriber", admissionTimeout: TimeSpan.FromSeconds(1));
        await using var publisher = (IAsyncDisposable)CreateTransport("publisher");

        var subscriberTransport = (INegotiationTransport)subscriber;
        var publisherTransport = (INegotiationTransport)publisher;

        using var publisherCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var ready = new TaskCompletionSource();
        var publisherTask = Task.Run(async () =>
        {
            ready.TrySetResult();
            var handled = 0;
            await foreach (var inbound in publisherTransport.ReceiveRequestsAsync(publisherCts.Token))
            {
                if (inbound is not INegotiationRequest request) continue;
                // First request: delay past the subscriber's AdmissionTimeout so the reply
                // arrives after _pendingReplies has already been cleaned up.
                if (handled == 0)
                    await Task.Delay(TimeSpan.FromSeconds(2), publisherCts.Token);
                await request.ReplyAsync(new NegotiationAck { Go = true }, publisherCts.Token);
                if (++handled >= 2) break;
            }
        }, TestContext.Current.CancellationToken);
        await ready.Task;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await subscriberTransport.SendCapabilityAsync(
                new Capability { DataTopic = DataTopic, InstanceId = "sub-1", WireNames = ["v1"] },
                TestContext.Current.CancellationToken));

        // Give the stale reply time to arrive and be dropped by the reply loop.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // If the loop crashed on the stale reply, this hangs until the outer TestContext token
        // cancels; if it survived, we get an ack promptly.
        var ack = await subscriberTransport.SendCapabilityAsync(
            new Capability { DataTopic = DataTopic, InstanceId = "sub-1", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);
        Assert.True(ack.Go);

        await publisherTask;
        await publisherCts.CancelAsync();
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task MalformedBodyIsDeadLettered()
    {
        await using var receiver = (IAsyncDisposable)CreateTransport("receiver");
        var receiverTransport = (INegotiationTransport)receiver;

        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var receiveTask = Task.Run(async () =>
        {
            await foreach (var _ in receiverTransport.ReceiveRequestsAsync(receiveCts.Token)) { }
        }, TestContext.Current.CancellationToken);

        // Inject a bad-JSON message directly on ctrl with all the routing properties the
        // subscription requires, so the message reaches our BuildRequest and hits the null path.
        await using (var client = new ServiceBusClient(_fixture.GetConnectionString()))
        await using (var sender = client.CreateSender(ControlTopic))
        {
            var bad = new ServiceBusMessage("this is not json"u8.ToArray())
            {
                MessageId = Guid.NewGuid().ToString(),
                Subject = "capability",
                SessionId = DataTopic,
                ReplyTo = "reply-" + ServiceName,
                ReplyToSessionId = "sub-bad",
            };
            bad.ApplicationProperties["data-topic"] = DataTopic;
            await sender.SendMessageAsync(bad, TestContext.Current.CancellationToken);
        }

        await using var dlqClient = new ServiceBusClient(_fixture.GetConnectionString());
        await using var dlqReceiver = dlqClient.CreateReceiver(
            ControlTopic,
            "request-" + ServiceName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var dlqMessage = await dlqReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.NotNull(dlqMessage);
        Assert.Equal("this is not json", dlqMessage.Body.ToString());

        await receiveCts.CancelAsync();
        try { await receiveTask; } catch (OperationCanceledException) { }
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task PayloadDataTopicMismatchIsDeadLettered()
    {
        await using var receiver = (IAsyncDisposable)CreateTransport("receiver");
        var receiverTransport = (INegotiationTransport)receiver;

        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var receiveTask = Task.Run(async () =>
        {
            await foreach (var _ in receiverTransport.ReceiveRequestsAsync(receiveCts.Token)) { }
        }, TestContext.Current.CancellationToken);

        // Route to session=DataTopic (matches our binding) but claim a different topic in the payload.
        await using (var client = new ServiceBusClient(_fixture.GetConnectionString()))
        await using (var sender = client.CreateSender(ControlTopic))
        {
            var capability = new Capability { DataTopic = "other-topic", InstanceId = "sub-liar", WireNames = ["v1"] };
            var msg = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(
                capability, NegotiationJsonContext.Default.Capability))
            {
                MessageId = Guid.NewGuid().ToString(),
                Subject = "capability",
                SessionId = DataTopic,
                ReplyTo = "reply-" + ServiceName,
                ReplyToSessionId = "sub-liar",
            };
            msg.ApplicationProperties["data-topic"] = DataTopic;
            await sender.SendMessageAsync(msg, TestContext.Current.CancellationToken);
        }

        await using var dlqClient = new ServiceBusClient(_fixture.GetConnectionString());
        await using var dlqReceiver = dlqClient.CreateReceiver(
            ControlTopic,
            "request-" + ServiceName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        var dlqMessage = await dlqReceiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.NotNull(dlqMessage);
        var recovered = JsonSerializer.Deserialize(dlqMessage.Body.ToArray(), NegotiationJsonContext.Default.Capability);
        Assert.Equal("other-topic", recovered!.DataTopic);

        await receiveCts.CancelAsync();
        try { await receiveTask; } catch (OperationCanceledException) { }
    }

    [Fact(Timeout = 90 * 1000)]
    public async Task SacContentionRetryTakesOverAfterHolderReleases()
    {
        await using var rawClient = new ServiceBusClient(_fixture.GetConnectionString());
        // Grab the Session lock
        var holder = await rawClient.AcceptSessionAsync(
            ControlTopic, "request-" + ServiceName, DataTopic,
            cancellationToken: TestContext.Current.CancellationToken);

        var received = new TaskCompletionSource<INegotiationRequest>();
        await using var transport = (IAsyncDisposable)CreateTransport("takeover");
        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var receiveTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var inbound in ((INegotiationTransport)transport).ReceiveRequestsAsync(receiveCts.Token))
                {
                    if (inbound is INegotiationRequest request)
                        received.TrySetResult(request);
                }
            }
            catch (OperationCanceledException) { }
        }, TestContext.Current.CancellationToken);

        // Let the transport hit its first retry cycle so we know it's in the contention path.
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        // Release the session lock
        await holder.DisposeAsync();

        await using var sender = rawClient.CreateSender(ControlTopic);
        await sender.SendMessageAsync(BuildCapabilityMessage("sub"), TestContext.Current.CancellationToken);
        var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal("sub", Assert.IsType<CapabilityRequest>(request).Capability.InstanceId);

        await receiveCts.CancelAsync();
        await receiveTask;
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task CancelDuringIdleAcceptExitsGracefully()
    {
        // Use a unique data-topic so no session is available and our loop is guaranteed to be
        // blocked in AcceptSessionAsync when cancellation fires.
        await using var receiver = (IAsyncDisposable)CreateTransport("receiver", dataTopic: "cancel-idle-" + Guid.NewGuid().ToString("N"));
        var receiverTransport = (INegotiationTransport)receiver;

        using var receiveCts = new CancellationTokenSource();
        var receiveTask = Task.Run(async () =>
        {
            await foreach (var _ in receiverTransport.ReceiveRequestsAsync(receiveCts.Token)) { }
        }, TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        await receiveCts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await receiveTask);
    }

    /// <summary>
    /// Verifies our design choice not to add explicit session-lock renewal on either the
    /// long-held request-side session or the long-held reply-side session. Both are acquired
    /// once and held for the transport's lifetime; we rely on the SDK's implicit
    /// renewal-on-receive during a blocked <c>ReceiveMessageAsync</c>. Uses a short-lock
    /// subscription (5s LockDuration on both request and reply) and runs a full roundtrip
    /// after a wait past that window. If either lock silently expired, the second roundtrip
    /// would hit its <c>AdmissionTimeout</c>.
    /// </summary>
    [Fact(Timeout = 90 * 1000)]
    public async Task LongIdleSessionsSurvivePastLockDuration()
    {
        await using var subscriber = (IAsyncDisposable)CreateTransport("subscriber", ShortLockServiceName);
        await using var publisher = (IAsyncDisposable)CreateTransport("publisher", ShortLockServiceName);

        var subscriberTransport = (INegotiationTransport)subscriber;
        var publisherTransport = (INegotiationTransport)publisher;

        using var publisherCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var ready = new TaskCompletionSource();
        var publisherTask = Task.Run(async () =>
        {
            ready.TrySetResult();
            var handled = 0;
            await foreach (var inbound in publisherTransport.ReceiveRequestsAsync(publisherCts.Token))
            {
                if (inbound is not INegotiationRequest request) continue;
                await request.ReplyAsync(new NegotiationAck { Go = true }, publisherCts.Token);
                if (++handled >= 2) break;
            }
        }, TestContext.Current.CancellationToken);
        await ready.Task;

        // First roundtrip starts both the subscriber's reply-session receiver and the
        // publisher's request-session receiver.
        var firstAck = await subscriberTransport.SendCapabilityAsync(
            new Capability { DataTopic = DataTopic, InstanceId = "sub-long-idle", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);
        Assert.True(firstAck.Go);

        // Idle past the subscriptions' 5s LockDuration. If either the request-side or reply-side
        // session lock silently expired, the second roundtrip below cannot complete.
        await Task.Delay(TimeSpan.FromSeconds(6), TestContext.Current.CancellationToken);

        var secondAck = await subscriberTransport.SendCapabilityAsync(
            new Capability { DataTopic = DataTopic, InstanceId = "sub-long-idle", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);
        Assert.True(secondAck.Go);

        await publisherTask;
        await publisherCts.CancelAsync();
    }

    internal INegotiationTransport CreateTransport(
        string processDisplayName,
        string serviceName = "negotiation",
        string dataTopic = DataTopic,
        TimeSpan? admissionTimeout = null)
    {
        var msgOpts = Options.Create(new MessagingOptions
        {
            AzureServiceBusConnectionString = _fixture.GetConnectionString(),
        });
        var negotiationOpts = Options.Create(new NegotiationOptions
        {
            ServiceName = serviceName,
            ProcessDisplayName = processDisplayName,
            Id = Guid.NewGuid(),
            AdmissionTimeout = admissionTimeout ?? TimeSpan.FromSeconds(15),
        });
        return new AzureServiceBusNegotiationTransport(msgOpts, negotiationOpts, dataTopic);
    }

    private static ServiceBusMessage BuildCapabilityMessage(string instanceId)
    {
        var capability = new Capability { DataTopic = DataTopic, InstanceId = instanceId, WireNames = ["v1"] };
        var msg = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(
            capability, NegotiationJsonContext.Default.Capability))
        {
            MessageId = Guid.NewGuid().ToString(),
            Subject = "capability",
            SessionId = DataTopic,
            ReplyTo = "reply-negotiation",
            ReplyToSessionId = instanceId,
        };
        msg.ApplicationProperties["data-topic"] = DataTopic;
        return msg;
    }
}
