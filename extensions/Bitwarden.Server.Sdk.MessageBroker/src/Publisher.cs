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
    /// message. The framework's fluent chain is the only in-assembly caller; derivations
    /// (custom transports, test doubles) override to implement wire transport but should not
    /// call this from their own user-facing surface.
    /// </summary>
    protected internal abstract Task SendAsync(
        IReadOnlyList<Payload<TPayload>.IVariant> variants,
        CancellationToken cancellationToken);

    /// <summary>
    /// Begins a publish from <paramref name="ceiling"/>. Chain
    /// <see cref="PublisherExtensions.With"/> once per assisted downcast and finish with
    /// <see cref="PublisherExtensions.SendAsync"/>.
    /// </summary>
    public PublishBuilder<TCeiling> Publish(TCeiling ceiling)
        => new([ceiling], ceiling, this);

    /// <summary>
    /// In-progress state of a publish. Constructed by <see cref="Publisher{TPayload, TCeiling}.Publish"/>
    /// and consumed by <see cref="PublisherExtensions.With"/> / <see cref="PublisherExtensions.SendAsync"/>.
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
    /// Finalizes the chain and hands it to the publisher. Only compiles once every assisted downcast
    /// has been crossed — until then the current position cannot reach the floor.
    /// </summary>
    public static Task SendAsync<TPayload, TCeiling, TCurrent>(
        this Publisher<TPayload, TCeiling>.PublishBuilder<TCurrent> builder,
        CancellationToken cancellationToken = default)
        where TPayload : PayloadCeiling<TPayload, TCeiling>, IPayloadVariants<TPayload>
        where TCeiling : Payload<TPayload>.ICeiling
        where TCurrent : Payload<TPayload>.IReachesFloor
    {
        builder.Current.DescendToFloor(builder.Accumulated);
        return builder.Publisher.SendAsync(builder.Accumulated, cancellationToken);
    }
}
