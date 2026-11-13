using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public sealed record OtherItem(int Id) : OtherItemPayload.ISole;

public class OtherItemPayload : Payload<OtherItemPayload, OtherItem, OtherItem>, IPayloadVariants<OtherItemPayload>, IPayloadWireName<OtherItemPayload>
{
    public static string WireName => "other-item";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(OtherItem), nameof(OtherItem))];
}

// A third payload that deliberately collides with MyItemPayload.WireName so the (topic, wire name)
// conflict path can be exercised. Lives here rather than in a separate file so the collision is
// visible in one place.
public sealed record WireNameCollisionItem(int Id) : WireNameCollisionPayload.ISole;

public class WireNameCollisionPayload : Payload<WireNameCollisionPayload, WireNameCollisionItem, WireNameCollisionItem>, IPayloadVariants<WireNameCollisionPayload>, IPayloadWireName<WireNameCollisionPayload>
{
    // Deliberately matches MyItemPayload.WireName so the conflict surfaces at DI time.
    public static string WireName => MyItemPayload.WireName;
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(WireNameCollisionItem), nameof(WireNameCollisionItem))];
}

/// <summary>
/// Pins the DI-time registration invariants for the topic-per-service topology:
/// one payload per (topic, family wire name), one payload per (topic, subscription name).
/// Distinct families coexist on one topic; distinct subscriptions coexist on one topic.
/// </summary>
public class TopicRegistrationConflictTests
{
    [Fact]
    public void AddPublisher_DifferentFamiliesOnSameTopic_Coexist()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("service");
        services.AddPublisher<OtherItemPayload, OtherItem>("service");
    }

    [Fact]
    public void AddPublisher_SameWireNameOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("service");

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPublisher<WireNameCollisionPayload, WireNameCollisionItem>("service"));

        Assert.Contains("service", ex.Message);
        Assert.Contains(MyItemPayload.WireName, ex.Message);
        Assert.Contains(nameof(MyItemPayload), ex.Message);
        Assert.Contains(nameof(WireNameCollisionPayload), ex.Message);
    }

    [Fact]
    public void AddSubscriber_DifferentFamiliesOnSameTopic_Coexist()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemPayload, MyItem>("service", "my-workers");
        services.AddSubscriber<OtherItemPayload, OtherItem>("service", "other-workers");
    }

    [Fact]
    public void AddSubscriber_SameWireNameOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemPayload, MyItem>("service", "my-workers");

        Assert.Throws<InvalidOperationException>(
            () => services.AddSubscriber<WireNameCollisionPayload, WireNameCollisionItem>("service", "other-workers"));
    }

    [Fact]
    public void AddSubscriber_SameSubscriptionNameDifferentPayload_Throws()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemPayload, MyItem>("service", "workers");

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddSubscriber<OtherItemPayload, OtherItem>("service", "workers"));

        Assert.Contains("workers", ex.Message);
        Assert.Contains("service", ex.Message);
        Assert.Contains(nameof(MyItemPayload), ex.Message);
        Assert.Contains(nameof(OtherItemPayload), ex.Message);
    }

    [Fact]
    public void DifferentPayloadsOnDifferentTopics_Coexist()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("orders");
        services.AddPublisher<OtherItemPayload, OtherItem>("invoices");
    }

    [Fact]
    public void AddSubscriber_AfterPublisherOnSameTopicAndFamily_Coexists()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemPayload, MyItem>("orders");
        services.AddSubscriber<MyItemPayload, MyItem>("orders", "self-consume");
    }

    [Fact]
    public void AddPublisher_AfterSubscriberOnSameTopicAndFamily_Coexists()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemPayload, MyItem>("orders", "self-consume");
        services.AddPublisher<MyItemPayload, MyItem>("orders");
    }
}
