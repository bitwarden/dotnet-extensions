using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Unit-level coverage of <c>NegotiationListenerCoordinator</c>'s backend-selection logic.
/// The ASB / Rabbit "listeners are actually processing messages" paths need real brokers and
/// are exercised by the shared <c>NegotiationTransportBehaviorTests</c> plus the per-backend
/// end-to-end tests; here we only pin the two skip-negotiation branches.
/// </summary>
public class NegotiationListenerCoordinatorTests
{
    [Fact]
    public async Task InMemoryChannelBackendSpawnsNoListeners()
    {
        // No ASB connection string, no Rabbit URI — the in-memory channel backend is selected
        // by MessagingOptions, and negotiation is skipped by design (one assembly version, no skew).
        var coordinator = Build(
            markers: [new PublisherRoleMarker("topic")],
            messaging: new MessagingOptions());

        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, coordinator.ListenerCount);
        await coordinator.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NoPublisherMarkersSpawnsNoListeners()
    {
        // Subscriber-only service: no AddPublisher calls, so no publisher-role markers.
        // Nothing on this side of the fleet to admit, so no listeners even with a distributed backend.
        var coordinator = Build(
            markers: [],
            messaging: new MessagingOptions { RabbitUri = "amqp://example" });

        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, coordinator.ListenerCount);
        await coordinator.StopAsync(TestContext.Current.CancellationToken);
    }

    private static NegotiationListenerCoordinator Build(
        IReadOnlyList<PublisherRoleMarker> markers,
        MessagingOptions messaging) =>
        new(
            markers,
            Options.Create(messaging),
            Options.Create(new NegotiationOptions { ServiceName = "svc", ProcessDisplayName = "test" }),
            new UnusedServiceProvider());

    // Never touched — the skip-negotiation branches short-circuit before lazy state resolution.
    // If a future coordinator change violates that, the test fails loudly.
    private sealed class UnusedServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new InvalidOperationException();
    }
}
