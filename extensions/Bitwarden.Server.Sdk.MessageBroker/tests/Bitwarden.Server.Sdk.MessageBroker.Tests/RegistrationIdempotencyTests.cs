using Bitwarden.Server.Sdk.MessageBroker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

[Collection("InMemory")]
/// <summary>
/// Verifies that calling AddPublisher, AddSubscriber, and AddMessageConsumer multiple times with
/// the same arguments produces the same set of service registrations as calling them once.
/// Duplicate IHostedService registrations are particularly dangerous because the host calls
/// StartAsync and StopAsync on every registered instance.
/// </summary>
public class RegistrationIdempotencyTests
{
    [Fact]
    public void AddPublisher_CalledTwice_DoesNotDuplicateKeyedServices()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItem>("test");
        var publisherCount = Count<IPublisher<MyItem>>(services, "test");
        var serializerCount = Count<IMessageSerializer>(services, "test");
        var hostedServiceCount = CountHostedServices(services);

        services.AddPublisher<MyItem>("test");

        Assert.Equal(publisherCount, Count<IPublisher<MyItem>>(services, "test"));
        Assert.Equal(serializerCount, Count<IMessageSerializer>(services, "test"));
        Assert.Equal(hostedServiceCount, CountHostedServices(services));
    }

    [Fact]
    public void AddSubscriber_CalledTwice_DoesNotDuplicateKeyedServicesOrHostedServices()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItem>("test", "group");
        var subscriberCount = Count<ISubscriber<MyItem>>(services, "test/group");
        var hostedServiceCount = CountHostedServices(services);

        services.AddSubscriber<MyItem>("test", "group");

        Assert.Equal(subscriberCount, Count<ISubscriber<MyItem>>(services, "test/group"));
        Assert.Equal(hostedServiceCount, CountHostedServices(services));
    }

    [Fact]
    public void AddMessageConsumer_CalledTwice_DoesNotDuplicateHostedServices()
    {
        var services = new ServiceCollection();
        services.AddMessageConsumer<MyItem, NullConsumer>("test", "group");
        var hostedServiceCount = CountHostedServices(services);

        services.AddMessageConsumer<MyItem, NullConsumer>("test", "group");

        Assert.Equal(hostedServiceCount, CountHostedServices(services));
    }

    [Fact]
    public void AddMessageConsumer_SameConsumerTypeDifferentSubscriptions_RegistersBothHostedServices()
    {
        // The same consumer class may handle messages from multiple subscriptions.
        // Each (TConsumer, subscriptionKey) pair must get its own background service.
        var services = new ServiceCollection();
        services.AddMessageConsumer<MyItem, NullConsumer>("test", "group-a");
        var hostedServiceCountAfterFirst = CountHostedServices(services);

        services.AddMessageConsumer<MyItem, NullConsumer>("test", "group-b");

        // One additional ConsumerBackgroundService is expected; ChannelTopic is shared (same topic name).
        Assert.Equal(hostedServiceCountAfterFirst + 1, CountHostedServices(services));
    }

    [Fact]
    public void AddPublisherThenAddSubscriber_ChannelTopicHostedServiceRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItem>("test");
        services.AddSubscriber<MyItem>("test", "group");
        var hostedServiceCount = CountHostedServices(services);

        // Calling either again must not grow the IHostedService list.
        services.AddPublisher<MyItem>("test");
        services.AddSubscriber<MyItem>("test", "group");

        Assert.Equal(hostedServiceCount, CountHostedServices(services));
    }

    [Fact]
    public void AddSubscriberThenAddPublisher_ProducesSameHostedServiceCountAsOppositeOrder()
    {
        var servicesA = new ServiceCollection();
        servicesA.AddPublisher<MyItem>("test");
        servicesA.AddSubscriber<MyItem>("test", "group");

        var servicesB = new ServiceCollection();
        servicesB.AddSubscriber<MyItem>("test", "group");
        servicesB.AddPublisher<MyItem>("test");

        Assert.Equal(CountHostedServices(servicesA), CountHostedServices(servicesB));
    }

    private static int Count<T>(IServiceCollection services, string key) =>
        services.Count(d => d.ServiceType == typeof(T) && (string?)d.ServiceKey == key);

    private static int CountHostedServices(IServiceCollection services) =>
        services.Count(d => d.ServiceType == typeof(IHostedService));

    private sealed class NullConsumer : IMessageConsumer<MyItem>
    {
        public Task HandleAsync(Envelope<MyItem> envelope, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
