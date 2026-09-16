using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class AzureServiceBusNegotiationTransportTests
    : NegotiationTransportBehaviorTests, IClassFixture<AzureServiceBusFixture>
{
    private readonly AzureServiceBusFixture _fixture;

    public AzureServiceBusNegotiationTransportTests(AzureServiceBusFixture fixture)
    {
        _fixture = fixture;
    }

    internal override Task<INegotiationTransport> CreateTransportAsync(string processDisplayName)
    {
        var msgOpts = Options.Create(new MessagingOptions
        {
            AzureServiceBusConnectionString = _fixture.GetConnectionString(),
        });
        var negotiationOpts = Options.Create(new NegotiationOptions
        {
            ServiceName = ServiceName,
            ProcessDisplayName = processDisplayName,
            Id = Guid.NewGuid(),
            AdmissionTimeout = TimeSpan.FromSeconds(15),
        });
        return Task.FromResult<INegotiationTransport>(
            new AzureServiceBusNegotiationTransport(msgOpts, negotiationOpts));
    }

    // Session-enabled subscriptions on the shared emulator carry state between sequential
    // tests. Drain each before every test — session receivers must be acquired one at a time
    // with a short cancellation so we exit fast when nothing is left.
    public override async ValueTask InitializeAsync()
    {
        await using var client = new ServiceBusClient(_fixture.GetConnectionString());
        await DrainSessionsAsync(client, "ctrl", "request-" + ServiceName);
        await DrainSessionsAsync(client, "ctrl", "reply-" + ServiceName);
    }

    private static async Task DrainSessionsAsync(ServiceBusClient client, string topic, string subscription)
    {
        while (true)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            ServiceBusSessionReceiver receiver;
            try
            {
                receiver = await client.AcceptNextSessionAsync(
                    topic,
                    subscription,
                    new ServiceBusSessionReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete },
                    timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceTimeout)
            {
                break;
            }

            await using (receiver)
            {
                IReadOnlyList<ServiceBusReceivedMessage> batch;
                do
                {
                    batch = await receiver.ReceiveMessagesAsync(maxMessages: 100, maxWaitTime: TimeSpan.FromSeconds(1));
                } while (batch.Count > 0);
            }
        }
    }
}
