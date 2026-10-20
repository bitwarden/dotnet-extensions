using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

[Collection("InMemory")]
public class SerializerOptionsTests
{
    /// <summary>
    /// Verifies that per-topic JsonSerializerOptions (e.g. a custom naming policy) are respected
    /// for both serialization (publish) and deserialization (receive).
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task PerTopicSerializerOptionsAreApplied()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItemPayload, MyItem>("test");
                services.AddSubscriber<MyItemPayload, MyItem>("test", "test");
                // Use snake_case so the serialized JSON uses "id" instead of "Id"
                // (camelCase would also change it — snake_case makes the intent clearer).
                services.Configure<MessageBrokerSerializerOptions>("test", opts =>
                    opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>("test");
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>("test/test");

        await publisher.Publish(new MyItem(42)).SendAsync(TestContext.Current.CancellationToken);

        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(42, envelope.Payload.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that serializer options are scoped per topic — configuring "topic-a" does not
    /// affect messages on "topic-b".
    /// </summary>
    [Fact(Timeout = 60 * 1000)]
    public async Task SerializerOptionsAreIsolatedPerTopic()
    {
        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddPublisher<MyItemPayload, MyItem>("topic-a");
                services.AddSubscriber<MyItemPayload, MyItem>("topic-a", "topic-a");
                services.AddPublisher<MyItemPayload, MyItem>("topic-b");
                services.AddSubscriber<MyItemPayload, MyItem>("topic-b", "topic-b");

                // Only topic-a gets the custom policy.
                services.Configure<MessageBrokerSerializerOptions>("topic-a", opts =>
                    opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisherA = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>("topic-a");
        var subscriberA = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>("topic-a/topic-a");
        var publisherB = host.Services.GetRequiredKeyedService<Publisher<MyItemPayload, MyItem>>("topic-b");
        var subscriberB = host.Services.GetRequiredKeyedService<ISubscriber<MyItemPayload, MyItem>>("topic-b/topic-b");

        await publisherA.Publish(new MyItem(1)).SendAsync(TestContext.Current.CancellationToken);
        await publisherB.Publish(new MyItem(2)).SendAsync(TestContext.Current.CancellationToken);

        await foreach (var envelope in subscriberA.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, envelope.Payload.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }

        await foreach (var envelope in subscriberB.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(2, envelope.Payload.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Verifies that <see cref="IMessageSerializer"/> construction fails with
    /// <see cref="InvalidOperationException"/> when the configured
    /// <see cref="System.Text.Json.JsonSerializerOptions"/> has no <c>TypeInfoResolver</c>.
    /// This path is exercised in AOT/trimming builds where reflection is disabled by default.
    /// </summary>
    [Fact]
    public void SerializerThrowsWhenTypeInfoResolverIsNull()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("test");
        // Replace the options with a JsonSerializerOptions that has no TypeInfoResolver.
        services.Configure<MessageBrokerSerializerOptions>("test", opts =>
            opts.JsonSerializerOptions = new JsonSerializerOptions());

        var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IMessageSerializer>("test"));
    }
}
