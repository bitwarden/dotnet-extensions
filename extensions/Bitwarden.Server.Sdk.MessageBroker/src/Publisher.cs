namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Publishes payloads of family <typeparamref name="TPayload"/> that name
/// <typeparamref name="TCeiling"/> as their ceiling. The compile-time pairing of TPayload and
/// TCeiling is enforced by <see cref="PayloadCeiling{TSelf, TCeiling}"/>: a payload can only
/// derive from one <c>PayloadCeiling&lt;TSelf, X&gt;</c>, so passing a rogue class that merely
/// implements <c>TPayload.ICeiling</c> — instead of the ceiling the payload actually declared —
/// fails <see cref="Publish"/>'s parameter check.
/// </summary>
public abstract class Publisher<TPayload, TCeiling>
    where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
    where TCeiling : Payload<TPayload>.ICeiling
{
    /// <summary>
    /// Sends <paramref name="variants"/> — the complete ceiling-to-floor sequence — as one
    /// message identified by <paramref name="messageId"/>. The framework's fluent chain is the
    /// only in-assembly caller; derivations (custom transports, test doubles) override to
    /// implement wire transport but should not call this from their own user-facing surface.
    /// </summary>
    /// <param name="variants">The variants to send, ceiling first.</param>
    /// <param name="messageId">
    /// The message's identifier, carried to every subscriber as <see cref="Envelope{TPayload, TCeiling}.MessageId"/>.
    /// Already validated by <see cref="PublisherExtensions"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the send.</param>
    protected internal abstract Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        string messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends <paramref name="body"/>, versions already serialized, exactly as given, under
    /// <paramref name="messageId"/>. For an outbox sending what it stored: decoding and re-encoding
    /// would drop any version the sending build doesn't know. The built-in publishers implement it.
    /// </summary>
    internal virtual Task SendRawAsync(ReadOnlyMemory<byte> body, string messageId, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{GetType().Name} can't send a serialized body.");

    /// <summary>
    /// Begins a publish from <paramref name="ceiling"/>. Chain
    /// <see cref="PublisherExtensions.With"/> once per assisted downcast and finish with one of
    /// the <see cref="PublisherExtensions"/> <c>SendAsync</c> overloads.
    /// </summary>
    public PublishBuilder<TCeiling> Publish(TCeiling ceiling)
        => new([ceiling], ceiling, this);

    /// <summary>
    /// In-progress state of a publish. Constructed by <see cref="Publisher{TPayload, TCeiling}.Publish"/>
    /// and consumed by <see cref="PublisherExtensions.With"/> and the <see cref="PublisherExtensions"/>
    /// <c>SendAsync</c> overloads.
    /// </summary>
    public readonly struct PublishBuilder<TCurrent>
        where TCurrent : Payload<TPayload>.IVariant
    {
        internal List<Payload<TPayload>.IVariant> Accumulated { get; }
        internal TCurrent Current { get; }
        internal Publisher<TPayload, TCeiling> Publisher { get; }

        internal PublishBuilder(
            List<Payload<TPayload>.IVariant> accumulated,
            TCurrent current,
            Publisher<TPayload, TCeiling> publisher)
        {
            Accumulated = accumulated;
            Current = current;
            Publisher = publisher;
        }
    }
}


/// <summary>
/// Continuation methods on <see cref="Publisher{TPayload, TCeiling}.PublishBuilder{TCurrent}"/>. Extensions
/// rather than instance methods because they additionally constrain the builder's
/// <c>TCurrent</c> — something instance methods on a generic struct cannot do, since a method
/// cannot re-constrain a class-level type parameter. Extensions can, because the receiver's type
/// parameters are re-declared as method-level parameters on the extension.
/// </summary>
public static class PublisherExtensions
{
    /// <summary>
    /// Crosses the next assisted downcast. Any pure downcasts between the current position and that downcasts are
    /// taken automatically, so only the variants that actually need caller data appear at the call
    /// site.
    /// </summary>
    public static Publisher<TPayload, TCeiling>.PublishBuilder<TTo> With<TPayload, TCeiling, TCurrent, TFrom, TTo, TAssist>(
        this Publisher<TPayload, TCeiling>.PublishBuilder<TCurrent> builder,
        Payload<TPayload>.IAssist<TFrom, TTo, TAssist> assist)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TCurrent : Payload<TPayload>.IBlockedAt<TFrom>
        where TFrom : Payload<TPayload>.IAssistedDowncast<TTo, TAssist, TFrom>
        where TTo : Payload<TPayload>.IVariant
        where TAssist : Payload<TPayload>.IAssist<TFrom, TTo, TAssist>
    {
        var blocked = builder.Current.DescendToBlocked(builder.Accumulated);
        var next = assist.Descend(blocked);
        builder.Accumulated.Add(next);
        return new(builder.Accumulated, next, builder.Publisher);
    }

    /// <summary>
    /// The longest message identifier accepted on every transport: Azure Service Bus's limit.
    /// </summary>
    public const int MaxMessageIdLength = 128;

    /// <summary>
    /// Finalizes the chain and hands it to the publisher under a new message identifier (a
    /// version 7 GUID). Only compiles once every assisted downcast has been crossed — until then
    /// the current position cannot reach the floor.
    /// </summary>
    public static Task SendAsync<TPayload, TCeiling, TCurrent>(
        this Publisher<TPayload, TCeiling>.PublishBuilder<TCurrent> builder,
        CancellationToken cancellationToken = default)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TCurrent : Payload<TPayload>.IReachesFloor
        => builder.SendAsync(Guid.CreateVersion7().ToString(), cancellationToken);

    /// <summary>
    /// Finalizes the chain and hands it to the publisher under <paramref name="messageId"/>. Pass
    /// the same identifier every time the same message is sent again, so subscribers can recognize
    /// the repeat. Only compiles once every assisted downcast has been crossed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="messageId"/> is empty, whitespace, longer than <see cref="MaxMessageIdLength"/>,
    /// or not ASCII.
    /// </exception>
    public static Task SendAsync<TPayload, TCeiling, TCurrent>(
        this Publisher<TPayload, TCeiling>.PublishBuilder<TCurrent> builder,
        string messageId,
        CancellationToken cancellationToken = default)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TCurrent : Payload<TPayload>.IReachesFloor
    {
        ThrowIfInvalidMessageId(messageId);
        builder.Current.DescendToFloor(builder.Accumulated);
        return builder.Publisher.SendAsync(builder.Accumulated, messageId, cancellationToken);
    }

    internal static void ThrowIfInvalidMessageId(string messageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (messageId.Length > MaxMessageIdLength)
        {
            throw new ArgumentException(
                $"A message identifier can be at most {MaxMessageIdLength} characters.", nameof(messageId));
        }

        if (!System.Text.Ascii.IsValid(messageId))
        {
            throw new ArgumentException("A message identifier must be ASCII.", nameof(messageId));
        }
    }
}
