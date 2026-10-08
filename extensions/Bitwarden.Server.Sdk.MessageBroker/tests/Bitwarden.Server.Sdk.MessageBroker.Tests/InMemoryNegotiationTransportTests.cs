namespace Bitwarden.Server.Sdk.MessageBroker.Tests;

/// <summary>
/// Runs the shared <see cref="NegotiationTransportBehaviorTests"/> contract against
/// <see cref="InMemoryNegotiationTransport"/> so higher-level tests (actor, startup wiring)
/// can trust it as a drop-in substitute for the ASB and Rabbit transports.
/// </summary>
public class InMemoryNegotiationTransportTests : NegotiationTransportBehaviorTests
{
    private InMemoryNegotiationBroker _broker = null!;

    public override ValueTask InitializeAsync()
    {
        _broker = new InMemoryNegotiationBroker();
        return ValueTask.CompletedTask;
    }

    internal override Task<INegotiationTransport> CreateTransportAsync(string processDisplayName)
        => Task.FromResult<INegotiationTransport>(new InMemoryNegotiationTransport(_broker, dataTopic: "topic"));
}
