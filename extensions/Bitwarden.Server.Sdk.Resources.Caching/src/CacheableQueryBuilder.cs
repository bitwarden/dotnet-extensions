using System.Linq.Expressions;
using Bitwarden.Server.Sdk.Data;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Declares a cacheable query's read: how the row is found and shaped, who owns it, and how long its entry lives.
/// </summary>
/// <typeparam name="TEntity">The row it is read from.</typeparam>
/// <typeparam name="TResource">The resource.</typeparam>
/// <remarks>
/// <para>
/// An entry is the whole resource, every attribute, exactly as the service returns it to a caller holding the read
/// scope, but for the attributes <see cref="PerCaller"/> names: those are worked out on every read and never cached.
/// </para>
/// </remarks>
public sealed class CacheableQueryBuilder<TEntity, TResource>
    where TEntity : class
    where TResource : Resource
{
    internal CacheableQueryBuilder()
    {
    }

    internal StandardReadBuilder<TEntity, TResource> Read { get; } = new();

    internal TimeSpan CachedFor { get; private set; } = TimeSpan.FromMinutes(10);

    internal string VersionProperty { get; private set; } = VersionConcurrency.Property;

    internal CacheWrites WhenUnreachable { get; private set; } = CacheWrites.Refuse;

    internal List<string> PerCallerAttributes { get; } = [];

    internal List<CascadeDeclaration> CascadeDeclarations { get; } = [];

    internal Owner? Current { get; private set; }

    /// <summary>
    /// Shapes a row into the resource, from the row (and what <see cref="Including"/> loads with it) alone.
    /// </summary>
    /// <param name="map">The mapping. It may not capture anything: a map that reads the caller is a per-caller shape.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// Its answer, less the <see cref="PerCaller"/> attributes, is what the cache holds, so it must be the same for every
    /// caller who may read the row. Refused when it closes over anything, such as the acting user.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The map captures state.</exception>
    public CacheableQueryBuilder<TEntity, TResource> Map(Func<TEntity, TResource> map)
    {
        Read.Map(map);
        return this;
    }

    /// <summary>
    /// What else a read loads with the row, such as a collection the map reads.
    /// </summary>
    /// <param name="shape">Adds to the query with <c>Include</c>, and nothing else.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// A change to an included row moves the row's version on, so its entry is invalidated too. Only an unfiltered
    /// navigation of the row itself whose foreign key names the row can be invalidated so; anything else is refused as
    /// the host starts.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> Including(Func<IQueryable<TEntity>, IQueryable<TEntity>> shape)
    {
        Read.Including(shape);
        return this;
    }

    /// <summary>
    /// Declares a foreign key through which the database deletes or changes rows this read is built from, and why that
    /// leaves no entry stale.
    /// </summary>
    /// <typeparam name="TDependent">The row the key belongs to: the read's own, or one it includes.</typeparam>
    /// <param name="foreignKey">The key's property (<c>link =&gt; link.GroupId</c>), or properties in order
    /// (<c>link =&gt; new { link.A, link.B }</c>).</param>
    /// <param name="coverage">Why no entry is left stale.</param>
    /// <param name="reason">The reason in words, for the reader of the declaration.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// As the host starts, every foreign key whose delete behaviour is <c>Cascade</c> or <c>SetNull</c> on a table the
    /// read is built from must be declared. One undeclared refuses the host in <c>Development</c> and <c>Testing</c>,
    /// and is logged as a warning elsewhere. A declaration naming no such key, or <see cref="CascadeCoverage.ParentInvalidated"/>
    /// on a key that isn't its include's own, refuses the host everywhere.
    /// </remarks>
    /// <exception cref="ArgumentException">The key isn't a property of the row, or the reason is blank.</exception>
    public CacheableQueryBuilder<TEntity, TResource> CascadeCovered<TDependent>(
        Expression<Func<TDependent, object?>> foreignKey, CascadeCoverage coverage, string reason)
        where TDependent : class
    {
        ArgumentNullException.ThrowIfNull(foreignKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!Enum.IsDefined(coverage))
        {
            throw new ArgumentOutOfRangeException(nameof(coverage), coverage, null);
        }

        var row = foreignKey.Parameters[0];
        var body = foreignKey.Body is UnaryExpression { NodeType: ExpressionType.Convert } boxed ? boxed.Operand : foreignKey.Body;
        IReadOnlyList<Expression> members = body is NewExpression composite ? composite.Arguments : [body];
        var properties = members
            .Select(member => member is MemberExpression { Member: System.Reflection.PropertyInfo property } read && read.Expression == row
                ? property.Name
                : throw new ArgumentException(
                    $"A cascade is declared by the foreign key's properties of {typeof(TDependent).Name}, not {member}.", nameof(foreignKey)))
            .ToList();

        CascadeDeclarations.Add(new CascadeDeclaration(typeof(TDependent), properties, coverage, reason));
        return this;
    }

    /// <summary>
    /// Reads the resource by a unique key other than its identifier: an organization's entitlements, by the organization.
    /// </summary>
    /// <param name="key">The key's property.</param>
    /// <param name="name">What a caller calls the key: the name its handler takes it by.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// Read with <see cref="CacheableQuery{TEntity,TResource}.ReadByKeyAsync"/>, and "nothing recorded" under the key
    /// is cached as well, below every real version. The key must be unique in the schema.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> KeyedBy(Expression<Func<TEntity, Guid>> key, string name)
    {
        Read.KeyedBy(key, name);
        return this;
    }

    /// <summary>
    /// Reads the resource by a key a row may not have yet: a membership by its person, unknown until the invitation is
    /// accepted. A row without one has no entry under it.
    /// </summary>
    /// <param name="key">The key's property.</param>
    /// <param name="name">What a caller calls the key.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// The key must be unique in the schema, with the owner where it is <see cref="WithinOwner"/>, unless it is
    /// declared <see cref="NotUnique"/>.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> KeyedBy(Expression<Func<TEntity, Guid?>> key, string name)
    {
        Read.KeyedBy(key, name);
        return this;
    }

    /// <summary>
    /// The key names a row only within its owner: a membership by its person, in the organization that owns it.
    /// </summary>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// Entries are kept under <c>{service}:{type}:by-{key name}:{owner}:{key}</c>, the owner taken from the row's
    /// <c>[OwnedBy]</c> column; a read names the owner as row-level security does, from its context. "Nothing recorded"
    /// is cached as well, as its owner is known without a row.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> WithinOwner()
    {
        Read.WithinOwner();
        return this;
    }

    /// <summary>
    /// The key isn't unique in the schema, though a row is expected to hold it alone: for example a person's membership of an
    /// organization, where the schema has no unique index on the pair.
    /// </summary>
    /// <param name="order">Orders the rows under one key, the first being the one answered.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// A read takes up to two rows by <paramref name="order"/>. One is answered and cached; two are answered with the
    /// first, and nothing is cached; none is "nothing recorded", cached. A row arriving under a key (added, or its key
    /// set or moved there) marks every version of the key's entry dead until the tombstone expires: the entry may be
    /// another row's, whose version says nothing of this one's, so neither it nor "nothing recorded" is answered again
    /// from the cache while the arrival may have made two. That tombstone lives <c>Caching:Owned:CommitAllowance</c>,
    /// not the full lifetime: it need only refuse fills read before the arrival committed, and a read after it finds
    /// the one row, filled at its version, or two, for which nothing is filled.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> NotUnique(Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order)
    {
        Read.NotUnique(order);
        return this;
    }

    /// <summary>
    /// A read of <c>current</c> takes the key from the context: the person in context's membership
    /// (<see cref="Owner.User"/>), the organization in context's entitlements (<see cref="Owner.Organization"/>).
    /// </summary>
    /// <param name="axis">Which party the context names is the key.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// Read with <see cref="CacheableQuery{TEntity,TResource}.ReadCurrentAsync"/>. Its operation must require that
    /// party, and the owner too for a key <see cref="WithinOwner"/>; the contract publishes the key template with the
    /// party's placeholder (<c>{user}</c>) where the key's was, so a client fills it from the call's context.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> CurrentFrom(Owner axis)
    {
        Current = axis;
        return this;
    }

    /// <summary>
    /// The one of the row's several owners this read is about: a provider's link to an organization, read in the
    /// organization's context. An entry is checked against that owner.
    /// </summary>
    /// <param name="owner">An owner the row's <c>[OwnedBy]</c> declares.</param>
    /// <returns>The builder.</returns>
    public CacheableQueryBuilder<TEntity, TResource> OwnedBy(Owner owner)
    {
        Read.OwnedBy(owner);
        return this;
    }

    /// <summary>
    /// The row is its own owner: an organization, read by its identifier.
    /// </summary>
    /// <param name="owner">What kind of owner the row is.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// A request may name no owner of that kind; one that names another is refused <c>422</c>.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> IdentifiesOwner(Owner owner)
    {
        Read.IdentifiesOwner(owner);
        return this;
    }

    /// <summary>
    /// The row belongs to nobody: an account, read by its identifier by any holder of the scope.
    /// </summary>
    /// <returns>The builder.</returns>
    public CacheableQueryBuilder<TEntity, TResource> Unowned()
    {
        Read.Unowned();
        return this;
    }

    /// <summary>
    /// Reads past row-level security, for a management API that administers an owner it is not acting as.
    /// </summary>
    /// <param name="reason">Why, for the reader of the declaration.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// A cached read then checks the scope alone, as the service does, so no attribute marked <c>[PersonalData]</c> may
    /// be cached: such a resource is refused unless every one is <see cref="PerCaller"/>.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> AcrossOwners(string reason)
    {
        Read.AcrossOwners(reason);
        return this;
    }

    /// <summary>
    /// The attributes worked out on every read and never cached: what depends on the caller (the member's own key), or
    /// on rows the resource's version doesn't cover (provider staff, the organization's claim).
    /// </summary>
    /// <param name="attributes">The attributes, by their published names.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="CacheableQuery{TEntity,TResource}.CompleteAsync"/> works them out. A read asking for any of them (a read
    /// sending no fieldset asks for every attribute) is answered from the database, and is never answered from the
    /// cache by a client; a read asking for none of them may be. Published in the contract.
    /// </para>
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> PerCaller(params string[] attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        PerCallerAttributes.AddRange(attributes);
        return this;
    }

    /// <summary>
    /// How long an entry lives: the backstop for a missed invalidation, not the mechanism.
    /// </summary>
    /// <param name="lifetime">The lifetime. Ten minutes unless declared.</param>
    /// <returns>The builder.</returns>
    public CacheableQueryBuilder<TEntity, TResource> Lifetime(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        CachedFor = lifetime;
        return this;
    }

    /// <summary>
    /// The column that versions the row for the cache, where it is not <c>Version</c>.
    /// </summary>
    /// <param name="property">An <see cref="int"/> or <see cref="long"/> property of the row.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// For a row whose writes are last-writer-wins by design, as an account's are: a <c>Version</c>
    /// would be made a concurrency token by the platform, and refuse concurrent writes the service
    /// expects to succeed. A column of its own, which nothing but the cache moves, versions entries
    /// without that.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> VersionedBy(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        VersionProperty = property;
        return this;
    }

    /// <summary>
    /// What a change to a row this resource is built from does when the service cache can't be told of it before it
    /// commits.
    /// </summary>
    /// <param name="writes"><see cref="CacheWrites.Refuse"/> unless declared.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// <see cref="CacheWrites.Refuse"/> fails the change with <c>503</c> and commits nothing, so no reader of the cache
    /// grants what the change took away. <see cref="CacheWrites.Proceed"/> commits it with a warning, the entry stale
    /// until it expires: only for a resource no reader grants or refuses anything by. Where declarations reading one
    /// table disagree, <see cref="CacheWrites.Refuse"/> wins; the host logs which applies to each table as it starts.
    /// </remarks>
    public CacheableQueryBuilder<TEntity, TResource> WhenCacheUnreachable(CacheWrites writes)
    {
        if (!Enum.IsDefined(writes))
        {
            throw new ArgumentOutOfRangeException(nameof(writes), writes, null);
        }

        WhenUnreachable = writes;
        return this;
    }
}
