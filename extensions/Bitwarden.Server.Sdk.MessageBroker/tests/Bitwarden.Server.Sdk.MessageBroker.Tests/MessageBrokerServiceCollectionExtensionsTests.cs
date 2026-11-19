using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public class MessageBrokerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPublisher_DeclaresTopic()
    {
        var services = new ServiceCollection();

        services.AddPublisher<MyItem>("orders");

        var requirements = Collect(services);
        Assert.Equal(["messaging-topic"], requirements.Keys);
        Assert.Equal(["""{"topic":"orders"}"""], Describe(requirements, "messaging-topic"));
    }

    [Fact]
    public void AddSubscriber_DeclaresTopicAndSubscription()
    {
        var services = new ServiceCollection();

        services.AddSubscriber<MyItem>("orders", "billing");

        var requirements = Collect(services);
        Assert.Equal(["""{"topic":"orders"}"""], Describe(requirements, "messaging-topic"));
        Assert.Equal(["""{"topic":"orders","subscription":"billing"}"""], Describe(requirements, "messaging-subscription"));
    }

    [Fact]
    public void AddMessageConsumer_DeclaresTopicAndSubscription()
    {
        var services = new ServiceCollection();

        services.AddMessageConsumer<MyItem, NoopConsumer>("orders", "billing");

        var requirements = Collect(services);
        Assert.Equal(["""{"topic":"orders"}"""], Describe(requirements, "messaging-topic"));
        Assert.Equal(["""{"topic":"orders","subscription":"billing"}"""], Describe(requirements, "messaging-subscription"));
    }

    [Fact]
    public void PublisherAndSubscribers_OnSameTopic_DeclareTopicOnce()
    {
        var services = new ServiceCollection();

        services.AddPublisher<MyItem>("orders");
        services.AddSubscriber<MyItem>("orders", "billing");
        services.AddSubscriber<MyItem>("orders", "audit");
        services.AddSubscriber<MyItem>("orders", "billing");

        var requirements = Collect(services);
        Assert.Equal(["""{"topic":"orders"}"""], Describe(requirements, "messaging-topic"));
        Assert.Equal(
            ["""{"topic":"orders","subscription":"billing"}""", """{"topic":"orders","subscription":"audit"}"""],
            Describe(requirements, "messaging-subscription"));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> Collect(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<InfrastructureOptions>>().Value.Requirements;
    }

    private static IEnumerable<string> Describe(IReadOnlyDictionary<string, IReadOnlyList<JsonObject>> requirements, string kind)
        => requirements[kind].Select(requirement => requirement.ToJsonString());

    private sealed class NoopConsumer : IMessageConsumer<MyItem>
    {
        public Task HandleAsync(Envelope<MyItem> envelope, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
