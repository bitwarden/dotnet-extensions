using System.Runtime.CompilerServices;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Narrows an <see cref="INegotiationState"/> to a single data-topic. Its own methods take no
/// data-topic argument — the bound topic is supplied at construction, so a caller holding a
/// <see cref="TopicScopedNegotiationState"/> literally cannot ask for another topic's entries.
/// <para>
/// Used by the Azure Service Bus listener, which holds the single-active-consumer role for one
/// topic (session-id = topic) and must not read or write another topic's state. RabbitMQ uses
/// the unscoped <see cref="INegotiationState"/> directly because its one SAC-elected consumer
/// legitimately handles every published topic.
/// </para>
/// <para>
/// Also implements <see cref="INegotiationState"/> explicitly, so code paths that still
/// dispatch through the raw interface (e.g. <see cref="M:Microsoft.Extensions.DependencyInjection.NegotiationListener.RunAsync(System.Threading.CancellationToken)"/>)
/// keep working. Cross-topic calls through that surface throw <see cref="InvalidOperationException"/>.
/// </para>
/// </summary>
internal sealed class TopicScopedNegotiationState : INegotiationState
{
    public INegotiationState InnerState { get; }
    public string BoundTopic { get; }

    public TopicScopedNegotiationState(INegotiationState innerState, string boundTopic)
    {
        InnerState = innerState;
        BoundTopic = boundTopic;
    }

    // Topic-less surface. Callers holding this wrapper use these directly and cannot ask for a
    // different topic's entries — the type carries the scope.
    public IAsyncEnumerable<PublisherJoin> GetPublishersAsync(CancellationToken cancellationToken = default)
        => InnerState.GetPublishersAsync(BoundTopic, cancellationToken);

    public IAsyncEnumerable<Capability> GetSubscribersAsync(CancellationToken cancellationToken = default)
        => InnerState.GetSubscribersAsync(BoundTopic, cancellationToken);

    public Task<PublisherJoin?> TryGetPublisherAsync(string instanceId, CancellationToken cancellationToken = default)
        => InnerState.TryGetPublisherAsync(BoundTopic, instanceId, cancellationToken);

    public Task<Capability?> TryGetSubscriberAsync(string instanceId, CancellationToken cancellationToken = default)
        => InnerState.TryGetSubscriberAsync(BoundTopic, instanceId, cancellationToken);

    public Task UpsertPublisherAsync(PublisherJoin join, CancellationToken cancellationToken = default)
        => InnerState.UpsertPublisherAsync(BoundTopic, join, cancellationToken);

    public Task UpsertSubscriberAsync(Capability capability, CancellationToken cancellationToken = default)
        => InnerState.UpsertSubscriberAsync(BoundTopic, capability, cancellationToken);

    public Task RemovePublisherAsync(string instanceId, CancellationToken cancellationToken = default)
        => InnerState.RemovePublisherAsync(BoundTopic, instanceId, cancellationToken);

    public Task RemoveSubscriberAsync(string instanceId, CancellationToken cancellationToken = default)
        => InnerState.RemoveSubscriberAsync(BoundTopic, instanceId, cancellationToken);

    // Explicit INegotiationState implementation for consumers that still route through the
    // topic-parameterized interface. Validates the topic matches so a cross-topic call fails
    // fast instead of silently reading the wrong topic's entries.
    async IAsyncEnumerable<PublisherJoin> INegotiationState.GetPublishersAsync(
        string dataTopic,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        await foreach (var item in InnerState.GetPublishersAsync(dataTopic, cancellationToken))
            yield return item;
    }

    async IAsyncEnumerable<Capability> INegotiationState.GetSubscribersAsync(
        string dataTopic,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        await foreach (var item in InnerState.GetSubscribersAsync(dataTopic, cancellationToken))
            yield return item;
    }

    Task<PublisherJoin?> INegotiationState.TryGetPublisherAsync(string dataTopic, string instanceId, CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        return InnerState.TryGetPublisherAsync(dataTopic, instanceId, cancellationToken);
    }

    Task<Capability?> INegotiationState.TryGetSubscriberAsync(string dataTopic, string instanceId, CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        return InnerState.TryGetSubscriberAsync(dataTopic, instanceId, cancellationToken);
    }

    Task INegotiationState.UpsertPublisherAsync(string dataTopic, PublisherJoin join, CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        return InnerState.UpsertPublisherAsync(dataTopic, join, cancellationToken);
    }

    Task INegotiationState.UpsertSubscriberAsync(string dataTopic, Capability capability, CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        return InnerState.UpsertSubscriberAsync(dataTopic, capability, cancellationToken);
    }

    Task INegotiationState.RemovePublisherAsync(string dataTopic, string instanceId, CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        return InnerState.RemovePublisherAsync(dataTopic, instanceId, cancellationToken);
    }

    Task INegotiationState.RemoveSubscriberAsync(string dataTopic, string instanceId, CancellationToken cancellationToken)
    {
        Guard(dataTopic);
        return InnerState.RemoveSubscriberAsync(dataTopic, instanceId, cancellationToken);
    }

    private void Guard(string dataTopic)
    {
        if (!string.Equals(dataTopic, BoundTopic, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Negotiation state is scoped to data-topic '{BoundTopic}'; caller supplied '{dataTopic}'.");
    }
}
