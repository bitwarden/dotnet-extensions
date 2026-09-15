namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Access to the fleet-state cache used for message-plane version negotiation.
/// <para>
/// Does not handle concurrent writes, Users should ensure single-writers
/// </para>
/// </summary>
internal interface INegotiationState
{
    /// <summary>
    /// Streams every live publisher registered for the data-topic.
    /// </summary>
    IAsyncEnumerable<PublisherJoin> GetPublishersAsync(
        string dataTopic,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams every live subscriber registered for the data-topic. Same semantics as
    /// <see cref="GetPublishersAsync"/>.
    /// </summary>
    IAsyncEnumerable<Capability> GetSubscribersAsync(
        string dataTopic,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a publisher's cache entry with a fresh TTL
    /// </summary>
    Task UpsertPublisherAsync(
        string dataTopic,
        PublisherJoin join,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a subscriber's cache entry with a fresh TTL
    /// </summary>
    Task UpsertSubscriberAsync(
        string dataTopic,
        Capability capability,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a publisher's cache entry. Used on clean
    /// shutdown; TTL expiration handles the crash path.
    /// </summary>
    Task RemovePublisherAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a subscriber's cache entry. Used on clean
    /// shutdown; TTL expiration handles the crash path.
    /// </summary>
    Task RemoveSubscriberAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default);
}
