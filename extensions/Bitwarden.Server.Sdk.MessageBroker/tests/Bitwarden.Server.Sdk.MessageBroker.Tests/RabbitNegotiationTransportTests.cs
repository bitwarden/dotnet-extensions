using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class RabbitNegotiationTransportTests
    : NegotiationTransportBehaviorTests, IClassFixture<RabbitFixture>
{
    private readonly RabbitFixture _fixture;

    // Each simulated "process" in a test gets its own RabbitConnection, matching the production
    // one-connection-per-process model. Tracked here so the class can dispose them after the
    // transport (which only owns its channels) has released its references.
    private readonly List<RabbitConnection> _connections = [];

    public RabbitNegotiationTransportTests(RabbitFixture fixture)
    {
        _fixture = fixture;
    }

    internal override async Task<INegotiationTransport> CreateTransportAsync(string processDisplayName)
    {
        var msgOpts = Options.Create(new MessagingOptions { RabbitUri = _fixture.GetUri() });
        var negotiation = new NegotiationOptions
        {
            ServiceName = ServiceName,
            ProcessDisplayName = processDisplayName,
            Id = Guid.NewGuid(),
            AdmissionTimeout = TimeSpan.FromSeconds(15),
        };

        var connection = new RabbitConnection(msgOpts, []);
        await connection.StartingAsync(TestContext.Current.CancellationToken);
        _connections.Add(connection);
        // Bind every data-topic the behavior tests exercise so a single "service" acts as
        // both publisher and subscriber for them.
        return RabbitNegotiationTransport.ForListener(connection, Options.Create(negotiation), ["topic"]);
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var c in _connections) await c.DisposeAsync();
        _connections.Clear();
    }

    // The shared RabbitMQ container persists the request-<service> queue across tests. Declare
    // it (idempotent — matches the transport's args) and purge so each test starts from empty.
    public override async ValueTask InitializeAsync()
    {
        var factory = new ConnectionFactory { Uri = new Uri(_fixture.GetUri()) };
        await using var connection = await factory.CreateConnectionAsync(TestContext.Current.CancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);

        await channel.QueueDeclareAsync(
            queue: "request-" + ServiceName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-single-active-consumer"] = true },
            cancellationToken: TestContext.Current.CancellationToken);
        await channel.QueuePurgeAsync("request-" + ServiceName, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task SelfNegotiation_singleChannelHandlesRequestConsumeReplyConsumeAndPublish()
    {
        await using var transport = (IAsyncDisposable)await CreateTransportAsync("self");
        var negotiationTransport = (INegotiationTransport)transport;

        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var ready = new TaskCompletionSource();
        var receiveTask = Task.Run(async () =>
        {
            ready.TrySetResult();
            await foreach (var inbound in negotiationTransport.ReceiveRequestsAsync(receiveCts.Token))
            {
                if (inbound is not INegotiationRequest request) continue;
                await request.ReplyAsync(new NegotiationAck { Go = true }, receiveCts.Token);
                break;
            }
        }, TestContext.Current.CancellationToken);
        await ready.Task;

        var ack = await negotiationTransport.SendJoinAsync(
            new PublisherJoin { DataTopic = "topic", InstanceId = "self", WireNames = ["v1"] },
            TestContext.Current.CancellationToken);

        Assert.True(ack.Go);
        Assert.Empty(ack.Offenders);
        await receiveTask;

        await receiveCts.CancelAsync();
    }
}
