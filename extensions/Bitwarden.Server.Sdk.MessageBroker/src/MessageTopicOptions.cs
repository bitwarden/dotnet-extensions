namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Collects the subscription names registered for a topic via <see cref="MessageBrokerServiceCollectionExtensions.AddSubscriber{T}"/>.</summary>
/// <remarks>Used at construction time to pre-create per-subscriber resources so messages are not dropped before any subscriber starts.</remarks>
internal sealed class MessageTopicOptions<T>
{
    public HashSet<string> SubscriptionNames { get; } = [];
}
