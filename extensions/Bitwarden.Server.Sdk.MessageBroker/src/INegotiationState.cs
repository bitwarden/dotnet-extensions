namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Access to the fleet-state cache used for message-plane version negotiation. Implementations
/// do not serialize concurrent writes; callers must ensure a single writer per data-topic (the
/// negotiation transport's single-active-consumer semantics provide this).
/// </summary>
internal interface INegotiationState
{
    /// <summary>Streams every live publisher registered for the data-topic.</summary>
    IAsyncEnumerable<PublisherJoin> GetPublishersAsync(
        string dataTopic,
        CancellationToken cancellationToken = default);

    /// <summary>Streams every live subscriber registered for the data-topic.</summary>
    IAsyncEnumerable<Capability> GetSubscribersAsync(
        string dataTopic,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Direct lookup of a single publisher by instance identifier. Returns <c>null</c> if the
    /// entry does not exist or has expired. Cheaper than <see cref="GetPublishersAsync"/> for
    /// per-instance short-circuits.
    /// </summary>
    Task<PublisherJoin?> TryGetPublisherAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>Direct lookup of a single subscriber by instance identifier.</summary>
    Task<Capability?> TryGetSubscriberAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>Writes a publisher's cache entry with a fresh TTL.</summary>
    Task UpsertPublisherAsync(
        string dataTopic,
        PublisherJoin join,
        CancellationToken cancellationToken = default);

    /// <summary>Writes a subscriber's cache entry with a fresh TTL.</summary>
    Task UpsertSubscriberAsync(
        string dataTopic,
        Capability capability,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a publisher's cache entry. Called on clean shutdown; TTL expiration handles the
    /// crash path.
    /// </summary>
    Task RemovePublisherAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a subscriber's cache entry. Called on clean shutdown; TTL expiration handles the
    /// crash path.
    /// </summary>
    Task RemoveSubscriberAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default);
}
