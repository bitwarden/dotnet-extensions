using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

[Collection(AzureServiceBusCollection.Name)]
public class AzureServiceBusLockRenewalTests : IAsyncLifetime
{
    private static readonly TimeSpan PastTheLock = TimeSpan.FromSeconds(40);

    private readonly AzureServiceBusFixture _fixture;
    private IHost? _host;

    public AzureServiceBusLockRenewalTests(AzureServiceBusFixture fixture) => _fixture = fixture;

    public async ValueTask InitializeAsync()
    {
        await using var client = new ServiceBusClient(_fixture.GetConnectionString());
        await DrainAsync(client.CreateReceiver("behavior", "behavior", Drain));
        await DrainAsync(client.CreateReceiver("commands", Drain));
    }

    private static readonly ServiceBusReceiverOptions Drain = new() { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete };

    private static async Task DrainAsync(ServiceBusReceiver receiver)
    {
        await using var _ = receiver;
        while ((await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(1))).Count > 0) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is null) return;
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<IHost> StartHostAsync(Action<IServiceCollection> configure, Dictionary<string, string?>? settings = null)
    {
        var config = new Dictionary<string, string?> { ["AzureServiceBusConnectionString"] = _fixture.GetConnectionString(), ["VerifyTopologyAtStartup"] = "false" };
        foreach (var (key, value) in settings ?? []) config[key] = value;
        _host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(config))
            .ConfigureServices(services =>
            {
                services.AddSingleton<SlowHandling>();
                configure(services);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();
        await _host.StartAsync(TestContext.Current.CancellationToken);
        return _host;
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task ASlowTopicConsumerKeepsItsMessage()
    {
        var host = await StartHostAsync(services =>
        {
            services.AddPublisher<MyItemPayload, MyItem>("behavior");
            services.AddMessageConsumer<MyItemPayload, MyItem, SlowItemConsumer>("behavior", "behavior");
        });
        var id = Random.Shared.Next();

        await host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>(MessageBrokerKeys.Publisher<MyItemPayload>("behavior"))
            .Publish(new MyItem(id)).SendAsync(TestContext.Current.CancellationToken);

        await host.Services.GetRequiredService<SlowHandling>().AssertHandledOnceAsync(id);
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task ASlowCommandConsumerKeepsItsCommand()
    {
        var host = await StartHostAsync(services =>
        {
            services.AddCommandSender<SendMailPayload, SendMail>("commands");
            services.AddCommandConsumer<SendMailPayload, SendMail, SlowMailConsumer>("commands");
        });
        var id = Random.Shared.Next();

        await host.Services.GetRequiredKeyedService<Publisher<SendMailPayload, SendMail>>(MessageBrokerKeys.Publisher<SendMailPayload>("commands"))
            .Publish(new SendMail(id)).SendAsync(TestContext.Current.CancellationToken);

        await host.Services.GetRequiredService<SlowHandling>().AssertHandledOnceAsync(id);
    }

    [Fact(Timeout = 120 * 1000)]
    public async Task AHandlerThatOutlastsTheRenewalBoundLosesItsCommand()
    {
        // Renewal stops after two seconds, so the 40-second handler's 30-second lock expires.
        var host = await StartHostAsync(services =>
        {
            services.AddCommandSender<SendMailPayload, SendMail>("commands");
            services.AddCommandConsumer<SendMailPayload, SendMail, SlowMailConsumer>("commands");
        }, new() { ["MaxLockRenewalDuration"] = "00:00:02" });
        var id = Random.Shared.Next();
        var handling = host.Services.GetRequiredService<SlowHandling>();

        await host.Services.GetRequiredKeyedService<Publisher<SendMailPayload, SendMail>>(MessageBrokerKeys.Publisher<SendMailPayload>("commands"))
            .Publish(new SendMail(id)).SendAsync(TestContext.Current.CancellationToken);

        while (!handling.Started.Any(s => s.Id == id && s.DeliveryCount == 2))
            await Task.Delay(200, TestContext.Current.CancellationToken);
    }

    public sealed class SlowHandling
    {
        public ConcurrentQueue<(int Id, int DeliveryCount)> Started { get; } = new();
        public ConcurrentQueue<int> Finished { get; } = new();

        public async Task HandleAsync(int id, int deliveryCount, CancellationToken cancellationToken)
        {
            Started.Enqueue((id, deliveryCount));
            await Task.Delay(PastTheLock, cancellationToken);
            Finished.Enqueue(id);
        }

        public async Task AssertHandledOnceAsync(int id)
        {
            while (!Finished.Contains(id))
                await Task.Delay(200, TestContext.Current.CancellationToken);
            // Time for a redelivery, had the lock been lost, and for completion to settle.
            await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var starts = Started.Where(s => s.Id == id).ToList();
            Assert.Equal([(id, 1)], starts);
        }
    }

    public sealed class SlowItemConsumer(SlowHandling handling) : IMessageConsumer<MyItemPayload, MyItem>
    {
        public Task HandleAsync(Envelope<MyItemPayload, MyItem> envelope, CancellationToken cancellationToken) =>
            handling.HandleAsync(envelope.Payload.Id, envelope.DeliveryCount, cancellationToken);
    }

    public sealed class SlowMailConsumer(SlowHandling handling) : IMessageConsumer<SendMailPayload, SendMail>
    {
        public Task HandleAsync(Envelope<SendMailPayload, SendMail> envelope, CancellationToken cancellationToken) =>
            handling.HandleAsync(envelope.Payload.Id, envelope.DeliveryCount, cancellationToken);
    }
}
