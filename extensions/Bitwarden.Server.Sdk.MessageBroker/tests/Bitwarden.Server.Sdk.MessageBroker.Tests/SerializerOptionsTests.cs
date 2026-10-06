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
                services.AddPublisher<MyItem>("test");
                services.AddSubscriber<MyItem>("test", "test");
                // Use snake_case so the serialized JSON uses "id" instead of "Id"
                // (camelCase would also change it — snake_case makes the intent clearer).
                services.Configure<MessageBrokerSerializerOptions>("test", opts =>
                    opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisher = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("test");
        var subscriber = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>("test/test");

        await publisher.PublishAsync(new MyItem(42), TestContext.Current.CancellationToken);

        await foreach (var envelope in subscriber.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(42, envelope.Message.Id);
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
                services.AddPublisher<MyItem>("topic-a");
                services.AddSubscriber<MyItem>("topic-a", "topic-a");
                services.AddPublisher<MyItem>("topic-b");
                services.AddSubscriber<MyItem>("topic-b", "topic-b");

                // Only topic-a gets the custom policy.
                services.Configure<MessageBrokerSerializerOptions>("topic-a", opts =>
                    opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

                services.AddOptions<MessagingOptions>().BindConfiguration("");
            })
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var publisherA = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("topic-a");
        var subscriberA = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>("topic-a/topic-a");
        var publisherB = host.Services.GetRequiredKeyedService<IPublisher<MyItem>>("topic-b");
        var subscriberB = host.Services.GetRequiredKeyedService<ISubscriber<MyItem>>("topic-b/topic-b");

        await publisherA.PublishAsync(new MyItem(1), TestContext.Current.CancellationToken);
        await publisherB.PublishAsync(new MyItem(2), TestContext.Current.CancellationToken);

        await foreach (var envelope in subscriberA.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(1, envelope.Message.Id);
            await envelope.CompleteAsync(TestContext.Current.CancellationToken);
            break;
        }

        await foreach (var envelope in subscriberB.SubscribeAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(2, envelope.Message.Id);
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
        services.AddPublisher<MyItem>("test");
        // Replace the options with a JsonSerializerOptions that has no TypeInfoResolver.
        services.Configure<MessageBrokerSerializerOptions>("test", opts =>
            opts.JsonSerializerOptions = new JsonSerializerOptions());

        var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredKeyedService<IMessageSerializer>("test"));
    }
}
