namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// Root of a family and the container for every variant-side interface. Concrete payload
/// types derive from <see cref="Family{TSelf, TFloor, TCeiling}"/>; variants and downcast assists
/// implement nested interfaces accessed through the derived type name (<c>MyFamily.IFloor&lt;TUp&gt;</c>,
/// <c>MyFamily.IPure&lt;TDown, TUp&gt;</c>, <c>MyFamily.IAssist&lt;TFrom, TTo, TSelf&gt;</c>).
/// </summary>
/// <remarks>
/// <para>
/// A family is a versioned message: successive variants form a chain from floor (the oldest known
/// shape) to ceiling (the newest). Publishers walk the chain downward so subscribers on older code
/// still see a variant they can decode; subscribers walk it upward to reach the shape they know how
/// to consume.
/// </para>
/// <para>
/// Descent and ascent are coupled: any variant that is not the ceiling must declare both a downcast
/// to its floor-side neighbor and an upcast to its ceiling-side neighbor, so no variant is
/// unreachable in either direction. The interface set enforces this at compile time as possible and validated
/// at runtime service registration where not (complete, sparse, linear).
/// </para>
/// <para>
/// A chain with a single variant uses <see cref="ISole"/>.
/// </para>
/// <para>
/// The floor of a variant chain uses <see cref="IFloor{TUp}"/>. Each middle variant uses one of three
/// interfaces, each indicating a different topology of chain. <see cref="IPure{TDown, TUp}"/> for when
/// the variant can be directly downcast to the floor without assistance. 
/// <see cref="IAssisted{TDown, TAssist, TUp, TSelfV}"/> for when a variant requires injection of
/// additional data or the form <c>TAssist</c> in order to downcast. 
/// <see cref="IPureAboveAssist{TDown, TBlocked, TUp}"/> indicates that, while this variant downcasts
/// purely to the next earlier version, <c>TBlocked</c> exists below this variant which will require
/// additional data. This last interface exists to encode the required publish data injection sites
/// into the type system.
/// </para>
/// </remarks>
public abstract class Family<TSelf> where TSelf : Family<TSelf>
{
    /// <summary>Base marker for anything that is a variant of this family.</summary>
    public interface IVariant;

    // -----------------------------------------------------------------------
    // Descent + ascent capabilities. Every non-sole variant declares one
    // descent capability, plus the pure-upcast capability if it is not the
    // ceiling.
    // -----------------------------------------------------------------------

    /// <summary>Pure downcasting from this variant reaches the floor.</summary>
    public interface IReachesFloor : IVariant
    {
        /// <summary>Mutates <paramref name="sink"/> to append every variant from this one down to the floor to <paramref name="sink"/>, floor last.</summary>
        void DescendToFloor(ICollection<IVariant> sink);
    }

    /// <summary>
    /// Pure downcasting from this variant stops at <typeparamref name="TBlocked"/>, which needs
    /// caller-supplied data to descend further.
    /// </summary>
    public interface IBlockedAt<TBlocked> : IVariant where TBlocked : IVariant
    {
        /// <summary>Mutates <paramref name="sink"/> to append every variant from this one down to the blocked downcast, blocked downcast last, and returns it.</summary>
        TBlocked DescendToBlocked(ICollection<IVariant> sink);
    }

    /// <summary>
    /// Non-generic base for <see cref="IPureUp{TUp}"/>. Enables runtime walking — the
    /// <see cref="Envelope{TFamily, TCeiling}"/> uses it to fold pure upcasts through unknown target types.
    /// </summary>
    public interface IPureUp : IVariant
    {
        /// <summary>Produces the ceiling-side neighbor of this variant. Returned as the base marker so runtime code can chain without knowing the concrete type.</summary>
        IVariant Upcast();
    }

    /// <summary>
    /// Pure upcast to <typeparamref name="TUp"/>. Chained upcasts are ordinary method calls:
    /// <c>v.Upcast().Upcast()</c>.
    /// </summary>
    public interface IPureUp<TUp> : IPureUp where TUp : IVariant
    {
        /// <summary>Produces the ceiling-side neighbor of this variant.</summary>
        new TUp Upcast();

        IVariant IPureUp.Upcast() => Upcast();
    }

    // -----------------------------------------------------------------------
    // Position markers
    // -----------------------------------------------------------------------

    /// <summary>The top of the chain: no upcast, publishing starts here.</summary>
    public interface ICeiling : IVariant;

    /// <summary>
    /// Technical base for floor positions. Users implement <see cref="IFloor{TUp}"/> or
    /// <see cref="ISole"/>; this interface exists so extensions can constrain on "floor" without
    /// spelling the upcast target.
    /// </summary>
    public interface IFloor : IReachesFloor
    {
        void IReachesFloor.DescendToFloor(ICollection<IVariant> sink) { }
    }

    /// <summary>The bottom of the chain, with a pure upcast to its ceiling-side neighbor.</summary>
    public interface IFloor<TUp> : IFloor, IPureUp<TUp> where TUp : IVariant;

    /// <summary>
    /// The only supported variant: simultaneously the floor and the ceiling. No upcast, no
    /// downcast.
    /// </summary>
    public interface ISole : IFloor, ICeiling;

    // -----------------------------------------------------------------------
    // Middle variant kinds — each pairs a descent kind with pure upcast.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Pure descent to <typeparamref name="TDown"/>, pure ascent to <typeparamref name="TUp"/>.
    /// </summary>
    public interface IPure<TDown, TUp> : IReachesFloor, IPureUp<TUp>
        where TDown : IReachesFloor
        where TUp : IVariant
    {
        /// <summary>Produces the floor-side neighbor of this variant.</summary>
        TDown Downcast();

        void IReachesFloor.DescendToFloor(ICollection<IVariant> sink)
        {
            var down = Downcast();
            sink.Add(down);
            down.DescendToFloor(sink);
        }
    }

    /// <summary>
    /// Pure descent to <typeparamref name="TDown"/> (which is blocked at
    /// <typeparamref name="TBlocked"/> below), pure ascent to <typeparamref name="TUp"/>.
    /// </summary>
    public interface IPureAboveAssist<TDown, TBlocked, TUp> : IBlockedAt<TBlocked>, IPureUp<TUp>
        where TDown : IBlockedAt<TBlocked>
        where TBlocked : IVariant
        where TUp : IVariant
    {
        /// <summary>Produces the floor-side neighbor of this variant.</summary>
        TDown Downcast();

        TBlocked IBlockedAt<TBlocked>.DescendToBlocked(ICollection<IVariant> sink)
        {
            var down = Downcast();
            sink.Add(down);
            return down.DescendToBlocked(sink);
        }
    }

    /// <summary>
    /// Technical base shared between <see cref="IAssisted{TDown, TAssist, TUp, TSelfV}"/> and
    /// <see cref="IAssistedCeiling{TDown, TAssist, TSelfV}"/> so an
    /// <see cref="IAssist{TFrom, TTo, TSelfA}"/> marker can bind against either. Users pick one
    /// of the two derived interfaces, not this one.
    /// </summary>
    public interface IAssistedDowncast<TDown, TAssist, TSelfV> : IBlockedAt<TSelfV>
        where TDown : IVariant
        where TAssist : IAssist<TSelfV, TDown, TAssist>
        where TSelfV : IAssistedDowncast<TDown, TAssist, TSelfV>
    {
        /// <summary>Produces the floor-side neighbors using caller-supplied <paramref name="assist"/>.</summary>
        TDown Downcast(TAssist assist);

        TSelfV IBlockedAt<TSelfV>.DescendToBlocked(ICollection<IVariant> sink) => (TSelfV)this;
    }

    /// <summary>
    /// Assisted descent to <typeparamref name="TDown"/> (needs <typeparamref name="TAssist"/>),
    /// pure ascent to <typeparamref name="TUp"/>.
    /// </summary>
    public interface IAssisted<TDown, TAssist, TUp, TSelfV> : IAssistedDowncast<TDown, TAssist, TSelfV>, IPureUp<TUp>
        where TDown : IVariant
        where TAssist : IAssist<TSelfV, TDown, TAssist>
        where TUp : IVariant
        where TSelfV : IAssisted<TDown, TAssist, TUp, TSelfV>;

    // -----------------------------------------------------------------------
    // Ceiling variant kinds — each pairs a descent kind with ICeiling. No upcast.
    // -----------------------------------------------------------------------

    /// <summary>Pure descent from the ceiling.</summary>
    public interface IPureCeiling<TDown> : IReachesFloor, ICeiling
        where TDown : IReachesFloor
    {
        /// <summary>Produces the floor-side neighbor of this variant.</summary>
        TDown Downcast();

        void IReachesFloor.DescendToFloor(ICollection<IVariant> sink)
        {
            var down = Downcast();
            sink.Add(down);
            down.DescendToFloor(sink);
        }
    }

    /// <summary>Pure descent from the ceiling, with an assisted downcast somewhere below.</summary>
    public interface IPureAboveAssistCeiling<TDown, TBlocked> : IBlockedAt<TBlocked>, ICeiling
        where TDown : IBlockedAt<TBlocked>
        where TBlocked : IVariant
    {
        /// <summary>Produces the floor-side neighbor of this variant.</summary>
        TDown Downcast();

        TBlocked IBlockedAt<TBlocked>.DescendToBlocked(ICollection<IVariant> sink)
        {
            var down = Downcast();
            sink.Add(down);
            return down.DescendToBlocked(sink);
        }
    }

    /// <summary>Assisted descent from the ceiling.</summary>
    public interface IAssistedCeiling<TDown, TAssist, TSelfV> : IAssistedDowncast<TDown, TAssist, TSelfV>, ICeiling
        where TDown : IVariant
        where TAssist : IAssist<TSelfV, TDown, TAssist>
        where TSelfV : IAssistedCeiling<TDown, TAssist, TSelfV>;

    // -----------------------------------------------------------------------
    // Assist marker
    // -----------------------------------------------------------------------

    /// <summary>
    /// Caller-supplied data that carries <typeparamref name="TFrom"/> down to
    /// <typeparamref name="TTo"/>. The (TFrom, TTo) pair rides in the assist's own type so that
    /// <c>Then</c> extensions can infer which downcast is being crossed. The pairing is locked in both
    /// directions: the variant names its assist type and the assist names its variant pair, so
    /// neither compiles unless the other agrees.
    /// </summary>
    public interface IAssist<TFrom, TTo, TSelfA>
        where TFrom : IAssistedDowncast<TTo, TSelfA, TFrom>
        where TTo : IVariant
        where TSelfA : IAssist<TFrom, TTo, TSelfA>
    {
        /// <summary>Applies this assist to <paramref name="from"/> to produce its floor-side neighbor.</summary>
        TTo Descend(TFrom from) => from.Downcast((TSelfA)this);
    }
}

/// <summary>
/// A family that names its ceiling but not its floor. Exists so <see cref="Publisher{TFamily, TCeiling}"/>
/// can constrain on the family identity and its declared ceiling without also having to spell the
/// floor at every publisher registration. Concrete family types derive from
/// <see cref="Family{TSelf, TFloor, TCeiling}"/>, which layers the floor on top of this.
/// </summary>
public abstract class FamilyCeiling<TSelf, TCeiling> : Family<TSelf>
    where TSelf : FamilyCeiling<TSelf, TCeiling>
    where TCeiling : Family<TSelf>.ICeiling;

/// <summary>
/// A family that names its floor and ceiling. Concrete family types derive from this class,
/// which forces them to also implement <see cref="IFamilyVariants{TSelf}"/> — the static list
/// of variant types the framework uses to route messages on deserialize without runtime
/// reflection — and <see cref="IFamilyWireName{TSelf}"/> — the stable family identifier that
/// subscriptions bind or filter against when one topic carries multiple families.
/// </summary>
public abstract class Family<TSelf, TFloor, TCeiling> : FamilyCeiling<TSelf, TCeiling>
    where TSelf : Family<TSelf, TFloor, TCeiling>, IFamilyVariants<TSelf>, IFamilyWireName<TSelf>
    where TFloor : Family<TSelf>.IFloor
    where TCeiling : Family<TSelf>.ICeiling;

/// <summary>
/// The static index of every variant type belonging to <typeparamref name="TSelf"/> paired with
/// its stable wire-format discriminator. Wire names are decoupled from
/// CLR type names so authors can freely rename or move variants without breaking existing
/// on-the-wire messages.
/// </summary>
/// <remarks>
/// <para>
/// Variant wire names identify shapes WITHIN a family. The family itself is identified
/// by <see cref="IFamilyWireName{TSelf}.WireName"/>, which only needs to be unique within a
/// topic — two variants named <c>"v1"</c> on different families coexist on the same topic.
/// </para>
/// <para>
/// Because the chain shape is only known to the author, the framework cannot walk it at compile
/// time. Listing the variant types once on the family class trades a small amount of author
/// ceremony for a fully AOT-compatible deserialize path.
/// </para>
/// </remarks>
public interface IFamilyVariants<TSelf> where TSelf : IFamilyVariants<TSelf>
{
    /// <summary>
    /// Every concrete variant type in this family's chain paired with the stable wire name that
    /// identifies it on the wire. Wire names must be unique within a chain.
    /// </summary>
    static abstract IReadOnlyList<(Type Type, string WireName)> Variants { get; }
}

/// <summary>
/// The stable identifier the broker uses to tell this family apart from other families
/// carried on the same topic. Publishers stamp it on every outgoing message; subscriptions bind
/// or filter against it so each subscription only receives one family's messages.
/// </summary>
/// <remarks>
/// Family wire names must be unique within a topic but can repeat across topics. They are
/// decoupled from CLR type names so a family can be renamed or moved without disturbing messages
/// already on the wire. See <see cref="IFamilyVariants{TSelf}"/> for the per-variant wire names
/// that live inside a family.
/// </remarks>
public interface IFamilyWireName<TSelf> where TSelf : IFamilyWireName<TSelf>
{
    /// <summary>
    /// The stable family identifier for this payload. Appears on the broker as a routing key
    /// (RabbitMQ) or application property (Azure Service Bus) and as the
    /// <c>messaging.family</c> observability tag.
    /// </summary>
    static abstract string WireName { get; }
}
