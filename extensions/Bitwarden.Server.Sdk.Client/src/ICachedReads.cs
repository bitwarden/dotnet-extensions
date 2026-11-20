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
///   <item><description>every attribute asked for is one the cache holds (none the service works out per caller; with no fieldset, every attribute), and the contract's entry format is this client's;</description></item>
///   <item><description>the call names the organization, user and provider the operation requires, and none it forbids;</description></item>
///   <item><description>the entry is not a tombstone, and is in a format this client knows;</description></item>
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
    /// <param name="fields">The attributes the caller wants, by their published names; none for every attribute.</param>
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
    /// Answers a read of <c>current</c> from the cache if it safely can: its key is the party the call carries, as the
    /// contract's key template names it (<c>{organization}</c>, <c>{user}</c>, <c>{provider}</c>).
    /// </summary>
    /// <param name="operationId">The read's operation in the owner's contract.</param>
    /// <param name="fields">The attributes the caller wants, by their published names; none for every attribute.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A hit, "nothing recorded", or a miss.</returns>
    /// <remarks>
    /// Uses up an <see cref="OutgoingCall"/> statement on a hit, as
    /// <see cref="ReadAsync(string, Guid, IReadOnlyCollection{string}, CancellationToken)"/> does.
    /// </remarks>
    Task<CachedAnswer> ReadCurrentAsync(
        string operationId,
        IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What the next call to the service would carry, its <see cref="OutgoingCall"/> statement included, without using
    /// the statement up: for a client deciding which of its owner's reads answers a call.
    /// </summary>
    /// <returns>The organization, person and provider.</returns>
    OutgoingCallContext NextCall();

    /// <summary>
    /// Answers a read from the cache as it would for a call carrying <paramref name="call"/>, using up no statement:
    /// for a client composing one answer from several of its owner's reads, each checked against the parties the
    /// answer is about, which the client derives from <see cref="NextCall"/> and what the reads before it answered.
    /// </summary>
    /// <param name="operationId">The read's operation in the owner's contract.</param>
    /// <param name="key">The identifier or key, for <c>{key}</c>; null for a read of <c>current</c>.</param>
    /// <param name="fields">The attributes wanted; none for every attribute.</param>
    /// <param name="call">The parties the read is checked against and keyed by.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A hit, "nothing recorded", or a miss.</returns>
    /// <remarks>
    /// The checks are those of every read: the scope, the fields, the context the operation requires and forbids, and
    /// the entry's owner. Once the composed answer stands, <see cref="Answered"/> uses up the statement the call it
    /// replaces would have used; when it doesn't, the call is made, and uses it.
    /// </remarks>
    Task<CachedAnswer> ReadAsync(
        string operationId,
        Guid? key,
        IReadOnlyCollection<string> fields,
        OutgoingCallContext call,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uses up the <see cref="OutgoingCall"/> statement, if any, for an answer composed from the cache in place of the
    /// call it would have covered.
    /// </summary>
    void Answered();
}
