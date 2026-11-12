using Bitwarden.Server.Sdk.Resources.Caching;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Reads another service's owned cache, for its client, and only where the service would have
/// given the same answer to the same caller.
/// </summary>
/// <typeparam name="TClient">The client the reads belong to.</typeparam>
/// <remarks>
/// <para>
/// Read-only by construction: nothing here writes or removes. A deployment can also give the
/// client's Redis credential no right to write the owner's prefix. A miss asks the service, which
/// fills.
/// </para>
/// <para>
/// Each check comes from the owner's contract (<c>x-bitwarden-cache</c>), which the client package
/// embeds, so nobody writes one by hand and none can disagree with the read the service serves:
/// </para>
/// <list type="number">
///   <item><description>the scope the operation demands is one this client's credential holds;</description></item>
///   <item><description>every attribute asked for is one the cache holds;</description></item>
///   <item><description>the call names the organization, user and provider the operation requires, and none it forbids;</description></item>
///   <item><description>the entry is not a tombstone;</description></item>
///   <item><description>the entry's owner is the one the call names (the organization this call would send).</description></item>
/// </list>
/// <para>
/// Any doubt is a miss: an unknown operation, an unreadable entry, Redis unreachable or slow. The
/// service is always right; the cache may only agree with it.
/// </para>
/// </remarks>
public interface ICachedReads<TClient>
{
    /// <summary>
    /// Answers a read from the cache if it safely can.
    /// </summary>
    /// <param name="operationId">The read's operation in the owner's contract.</param>
    /// <param name="key">The identifier, or the unique key, read.</param>
    /// <param name="fields">The attributes the caller wants, by their published names.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A hit, "nothing recorded", or a miss.</returns>
    /// <remarks>
    /// A hit uses up an <see cref="OutgoingCall"/> statement as the call it replaces would have; a
    /// miss leaves it for that call.
    /// </remarks>
    Task<CachedAnswer> ReadAsync(
        string operationId,
        Guid key,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a collection question the service indexes (<c>x-bitwarden-index</c>) from the cache,
    /// if it safely can: the whole answer, or nothing.
    /// </summary>
    /// <param name="operationId">The list operation that answers the question in the owner's contract.</param>
    /// <param name="question">The question's name: <c>widgets-of</c>.</param>
    /// <param name="subject">The principal the question is about, which the call must name.</param>
    /// <param name="fields">The attributes the caller wants of each resource.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Each resource object of the answer, with only the attributes asked for; null when the cache
    /// cannot answer, and the service must be asked.
    /// </returns>
    /// <remarks>
    /// The index is the owner's answer for this principal: it is read
    /// only for a call whose context names that principal, and as the question requires the rest of
    /// the context. Every resource it names must be cached, not a tombstone, hold the fields asked for,
    /// and still answer the question (its subject and the question's values), or the whole read is a
    /// miss. The resources' own owner is not compared with the call's: the question crosses owners,
    /// and the index is the owner's decision about which of them this principal sees.
    /// </remarks>
    Task<IReadOnlyList<string>?> ReadIndexAsync(
        string operationId,
        string question,
        Guid subject,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a question the service answers whole (<c>x-bitwarden-answer</c>) from the cache, if it
    /// safely can: the action's response body, "nothing" for its <c>404</c>, or a miss.
    /// </summary>
    /// <param name="operationId">The action that answers it in the owner's contract.</param>
    /// <param name="answer">The answer's name: <c>widget-by-name</c>.</param>
    /// <param name="key">
    /// The key, as the owner compares it: an identifier, or for an answer keyed by text the normalised
    /// text, which is digested here (<see cref="OwnedAnswerKey.Of(string, string, ReadOnlySpan{byte})"/>) and never sent to Redis as written.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// A hit carrying the body in <see cref="CachedAnswer.Resource"/>, "nothing recorded" when the
    /// service answered <c>404</c>, or a miss.
    /// </returns>
    /// <remarks>
    /// Read only where the client's credential holds the action's scope and the call's context is
    /// what the action demands. An answer has no owner to compare: the action answers any holder of
    /// its scope about anything its key names.
    /// </remarks>
    Task<CachedAnswer> ReadAnswerAsync(
        string operationId,
        string answer,
        string key,
        CancellationToken cancellationToken = default);
}
