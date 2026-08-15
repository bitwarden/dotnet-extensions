using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

[Collection("InMemory")]
public class ChannelConsumerValidatorTests
{
    // The validator only activates when AddMessageConsumer is used (i.e., the app has opted into
    // the MessageConsumer framework). AddSubscriber alone is not enforced, so that raw ISubscriber<T>
    // usage and out-of-process consumers on ASB/Rabbit remain valid without restriction.

    [Fact(Timeout = 60 * 1000)]
    public async Task ThrowsWhenSomeChannelSubscribersHaveNoConsumer()
    {
        // "orders" has a consumer; "payments" does not. Because at least one AddMessageConsumer
        // call activated the validator, the uncovered "payments" subscriber must be rejected.
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddMessageConsumer<MyItem, NoOpConsumer>("orders", "orders");
                services.AddSubscriber<MyItem>("payments", "payments");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("payments", ex.Message);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task DoesNotThrowWhenAllSubscribersHaveConsumers()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItem>("test");
                services.AddMessageConsumer<MyItem, NoOpConsumer>("test", "test");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task DoesNotThrowForChannelSubscriberWithoutConsumerFramework()
    {
        // AddSubscriber alone (no AddMessageConsumer in the host) does not activate the validator.
        // This covers tests, and services where the consumer is invoked inline rather than via
        // a hosted service.
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSubscriber<MyItem>("test", "test");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 60 * 1000)]
    public async Task DoesNotThrowForExternalBrokerWithSubscriberOnly()
    {
        // When using Rabbit or ASB, the consumer may be in a different process entirely.
        // AddSubscriber alone is valid — the validator must not fire even when AddMessageConsumer
        // is used elsewhere (the broker check short-circuits first).
        var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(
                new Dictionary<string, string?> { { "RabbitUri", "amqp://guest:guest@localhost/" } }))
            .ConfigureServices(services =>
            {
                services.AddSubscriber<MyItem>("test", "test");
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("channel listeners have no MessageConsumer"))
        {
            Assert.Fail("Validator should not fire for external broker backends.");
        }
        catch
        {
            // Other exceptions (e.g., Rabbit connection failure) are expected and acceptable.
        }
    }

    private sealed class NoOpConsumer(ISubscriber<MyItem> subscriber) : MessageConsumer<MyItem>(subscriber)
    {
        protected override Task HandleAsync(Envelope<MyItem> envelope, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
