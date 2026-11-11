using Microsoft.Extensions.DependencyInjection;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

public sealed record OtherItem(int Id) : OtherItemFamily.ISole;

public class OtherItemFamily : Family<OtherItemFamily, OtherItem, OtherItem>, IFamilyVariants<OtherItemFamily>, IFamilyWireName<OtherItemFamily>
{
    public static string WireName => "other-item";
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(OtherItem), nameof(OtherItem))];
}

// A third family that deliberately collides with MyItemFamily.WireName so the (topic, wire name)
// conflict path can be exercised. Lives here rather than in a separate file so the collision is
// visible in one place.
public sealed record WireNameCollisionItem(int Id) : WireNameCollisionFamily.ISole;

public class WireNameCollisionFamily : Family<WireNameCollisionFamily, WireNameCollisionItem, WireNameCollisionItem>, IFamilyVariants<WireNameCollisionFamily>, IFamilyWireName<WireNameCollisionFamily>
{
    // Deliberately matches MyItemFamily.WireName so the conflict surfaces at DI time.
    public static string WireName => MyItemFamily.WireName;
    public static IReadOnlyList<(Type, string)> Variants => [(typeof(WireNameCollisionItem), nameof(WireNameCollisionItem))];
}

/// <summary>
/// Pins the DI-time registration invariants for the topic-per-service topology:
/// one family per (topic, family wire name), one family per (topic, subscription name).
/// Distinct families coexist on one topic; distinct subscriptions coexist on one topic.
/// </summary>
public class TopicRegistrationConflictTests
{
    [Fact]
    public void AddPublisher_DifferentFamiliesOnSameTopic_Coexist()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemFamily, MyItem>("service");
        services.AddPublisher<OtherItemFamily, OtherItem>("service");
    }

    [Fact]
    public void AddPublisher_SameWireNameOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemFamily, MyItem>("service");

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddPublisher<WireNameCollisionFamily, WireNameCollisionItem>("service"));

        Assert.Contains("service", ex.Message);
        Assert.Contains(MyItemFamily.WireName, ex.Message);
        Assert.Contains(nameof(MyItemFamily), ex.Message);
        Assert.Contains(nameof(WireNameCollisionFamily), ex.Message);
    }

    [Fact]
    public void AddSubscriber_DifferentFamiliesOnSameTopic_Coexist()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemFamily, MyItem>("service", "my-workers");
        services.AddSubscriber<OtherItemFamily, OtherItem>("service", "other-workers");
    }

    [Fact]
    public void AddSubscriber_SameWireNameOnSameTopic_Throws()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemFamily, MyItem>("service", "my-workers");

        Assert.Throws<InvalidOperationException>(
            () => services.AddSubscriber<WireNameCollisionFamily, WireNameCollisionItem>("service", "other-workers"));
    }

    [Fact]
    public void AddSubscriber_SameSubscriptionNameDifferentFamily_Throws()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemFamily, MyItem>("service", "workers");

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddSubscriber<OtherItemFamily, OtherItem>("service", "workers"));

        Assert.Contains("workers", ex.Message);
        Assert.Contains("service", ex.Message);
        Assert.Contains(nameof(MyItemFamily), ex.Message);
        Assert.Contains(nameof(OtherItemFamily), ex.Message);
    }

    [Fact]
    public void DifferentFamilysOnDifferentTopics_Coexist()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemFamily, MyItem>("orders");
        services.AddPublisher<OtherItemFamily, OtherItem>("invoices");
    }

    [Fact]
    public void AddSubscriber_AfterPublisherOnSameTopicAndFamily_Coexists()
    {
        var services = new ServiceCollection();
        services.AddPublisher<MyItemFamily, MyItem>("orders");
        services.AddSubscriber<MyItemFamily, MyItem>("orders", "self-consume");
    }

    [Fact]
    public void AddPublisher_AfterSubscriberOnSameTopicAndFamily_Coexists()
    {
        var services = new ServiceCollection();
        services.AddSubscriber<MyItemFamily, MyItem>("orders", "self-consume");
        services.AddPublisher<MyItemFamily, MyItem>("orders");
    }
}
