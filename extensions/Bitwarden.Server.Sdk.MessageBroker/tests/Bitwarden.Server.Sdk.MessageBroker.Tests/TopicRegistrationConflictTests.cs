using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public sealed record OtherItem(int Id) : OtherItemPayload.ISole;

public class OtherItemPayload : Payload<OtherItemPayload, OtherItem, OtherItem>, IPayloadVariants<OtherItemPayload>
{
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(OtherItem), nameof(OtherItem))];
}

/// <summary>
/// Verifies that a single topic name cannot be registered against two distinct payload families.
/// The wire format has no payload discriminator, so co-mingling families on one topic would
/// produce silent misroutes on Azure Service Bus and RabbitMQ. The check fires at registration
/// regardless of which backend MessagingOptions ultimately selects.
/// </summary>
public class TopicRegistrationConflictTests
{
    [Fact]
    public void AddPublisher_AfterDifferentPayloadOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("shared");

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPublisher<OtherItemPayload, OtherItem>("shared"));

        Assert.Contains("shared", ex.Message);
        Assert.Contains(nameof(MyItemPayload), ex.Message);
        Assert.Contains(nameof(OtherItemPayload), ex.Message);
    }

    [Fact]
    public void AddSubscriber_AfterDifferentPayloadOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemPayload, MyItem>("shared", "group");

        Assert.Throws<InvalidOperationException>(
            () => services.AddSubscriber<OtherItemPayload, OtherItem>("shared", "group"));
    }

    [Fact]
    public void AddSubscriber_AfterDifferentPayloadPublishedOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("shared");

        Assert.Throws<InvalidOperationException>(
            () => services.AddSubscriber<OtherItemPayload, OtherItem>("shared", "group"));
    }

    [Fact]
    public void AddPublisher_AfterDifferentPayloadSubscribedOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemPayload, MyItem>("shared", "group");

        Assert.Throws<InvalidOperationException>(
            () => services.AddPublisher<OtherItemPayload, OtherItem>("shared"));
    }

    [Fact]
    public void DifferentPayloadsOnDifferentTopics_Register()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("orders");
        services.AddPublisher<OtherItemPayload, OtherItem>("invoices");
    }

    [Fact]
    public void AddSubscriber_AfterPublisherOnSameTopic_Register()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("orders");
        services.AddSubscriber<MyItemPayload, MyItem>("orders", "self-consume");
    }

    [Fact]
    public void AddPublisher_AfterSubscriberOnSameTopic_Register()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemPayload, MyItem>("orders", "self-consume");
        services.AddPublisher<MyItemPayload, MyItem>("orders");
    }
}
