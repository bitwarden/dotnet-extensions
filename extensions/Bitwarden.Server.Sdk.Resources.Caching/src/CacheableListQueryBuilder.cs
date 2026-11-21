using System.Linq.Expressions;
using System.Reflection;
using Bitwarden.Server.Sdk.Data;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Declares a cached read-many: every row of the party a request's context names that one fixed condition selects, in
/// one fixed order, kept as one entry.
/// </summary>
/// <typeparam name="TEntity">The one table it is read from.</typeparam>
/// <typeparam name="TResource">The resource each row is, which holds only what the row holds.</typeparam>
/// <remarks>
/// <para>
/// The declaration is all there is: whose rows (<see cref="KeyedBy(Expression{Func{TEntity, Guid?}}, Owner)"/>), which
/// (<see cref="Where"/>), in what order (<see cref="OrderBy"/>), how each is shaped (<see cref="Map"/>), under what
/// scope (<see cref="Scope"/>) and how many at most (<see cref="AtMost"/>). The platform runs the query, keeps the
/// entry, pages it and invalidates it.
/// </para>
/// <para>
/// Nothing in it is worked out per caller, and nothing is read from another table: a change to any row of the table,
/// for the party the row had before it and the one it has after, is all that can make the entry stale.
/// </para>
/// </remarks>
public sealed class CacheableListQueryBuilder<TEntity, TResource>
    where TEntity : class
    where TResource : Resource
{
    /// <summary>
    /// The most rows an entry holds when <see cref="AtMost"/> isn't declared: the platform's maximum page size.
    /// </summary>
    public const int DefaultAtMost = Querying.Page.MaximumSize;

    internal CacheableListQueryBuilder()
    {
    }

    internal List<CascadeDeclaration> CascadeDeclarations { get; } = [];

    internal LambdaExpression? Key { get; private set; }

    internal Owner? Party { get; private set; }

    internal Expression<Func<TEntity, bool>>? Rows { get; private set; }

    internal Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? Order { get; private set; }

    internal Func<TEntity, TResource>? MapToResource { get; private set; }

    internal string? ReadScope { get; private set; }

    internal int Most { get; private set; } = DefaultAtMost;

    internal TimeSpan CachedFor { get; private set; } = TimeSpan.FromMinutes(10);

    internal CacheWrites WhenUnreachable { get; private set; } = CacheWrites.Refuse;

    /// <summary>
    /// Whose rows it holds: those whose <paramref name="key"/> is the <paramref name="party"/> a request's context
    /// names, across every owner they belong to. A person's memberships, in every organization.
    /// </summary>
    /// <param name="key">The column naming the party.</param>
    /// <param name="party">The party the context names, which its operation requires and its key is.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// The read steps past row-level security, narrowed instead to the party in context alone; the entry's owner is
    /// that party, and a client checks it against the party its call carries. Any change to a row invalidates the
    /// entries of the party the column named before the change and the one it names after.
    /// </remarks>
    public CacheableListQueryBuilder<TEntity, TResource> KeyedBy(Expression<Func<TEntity, Guid?>> key, Owner party) =>
        Keyed(key, party);

    /// <summary>
    /// Whose rows it holds, by a column every row has.
    /// </summary>
    /// <param name="key">The column naming the party.</param>
    /// <param name="party">The party the context names.</param>
    /// <returns>The builder.</returns>
    public CacheableListQueryBuilder<TEntity, TResource> KeyedBy(Expression<Func<TEntity, Guid>> key, Owner party) =>
        Keyed(key, party);

    /// <summary>
    /// Which of the party's rows it holds: a condition on the row's own columns, the same for every caller.
    /// </summary>
    /// <param name="rows">The condition, as the database query applies it.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// Invalidation ignores it: a row that moves into the condition or out of it changes the entry as surely as one
    /// that changes within it, so any change to a row of the party invalidates it.
    /// </remarks>
    public CacheableListQueryBuilder<TEntity, TResource> Where(Expression<Func<TEntity, bool>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Rows = rows;
        return this;
    }

    /// <summary>
    /// The one order its rows are read in, ending in a unique column. The database orders them; a client only takes a
    /// page of them, as its collation may order text differently.
    /// </summary>
    /// <param name="order">Orders the rows.</param>
    /// <returns>The builder.</returns>
    public CacheableListQueryBuilder<TEntity, TResource> OrderBy(Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        Order = order;
        return this;
    }

    /// <summary>
    /// Shapes a row into the resource, from the row alone.
    /// </summary>
    /// <param name="map">The mapping. It may not capture anything.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="InvalidOperationException">The map captures state.</exception>
    public CacheableListQueryBuilder<TEntity, TResource> Map(Func<TEntity, TResource> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (map.Target is { } target
            && target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0)
        {
            throw new InvalidOperationException(
                $"{typeof(TResource).Name}'s cached read-many maps a row with a function that captures state. A map "
                + "takes the row and nothing else.");
        }

        MapToResource = map;
        return this;
    }

    /// <summary>
    /// The scope its operation demands, alone.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The builder.</returns>
    public CacheableListQueryBuilder<TEntity, TResource> Scope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ReadScope = scope;
        return this;
    }

    /// <summary>
    /// The most rows an entry holds. A party with more is never cached: every read of theirs is the database's.
    /// </summary>
    /// <param name="rows">The most rows, no more than the operation's maximum page size, so one page holds an entry.
    /// <see cref="DefaultAtMost"/> unless declared.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> is less than one.</exception>
    public CacheableListQueryBuilder<TEntity, TResource> AtMost(int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        Most = rows;
        return this;
    }

    /// <summary>
    /// Declares a database cascade through a foreign key of its rows, and why it leaves the entry current.
    /// </summary>
    /// <typeparam name="TDependent">The row the key belongs to: its own.</typeparam>
    /// <param name="foreignKey">The key's property (<c>row =&gt; row.OrganizationId</c>), or properties in order.</param>
    /// <param name="coverage">Why the entry is left current. A read-many includes nothing, so only
    /// <see cref="CascadeCoverage.DependentsLoaded"/> applies.</param>
    /// <param name="reason">The reason in words, for the reader of the declaration.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// As for a read of one (<see cref="CacheableQueryBuilder{TEntity,TResource}.CascadeCovered{TDependent}"/>): every
    /// foreign key whose delete behaviour is <c>Cascade</c> or <c>SetNull</c> on its table must be declared, as a row
    /// the database deletes or changes without the change tracker would leave the entry stale until it expires.
    /// </remarks>
    /// <exception cref="ArgumentException">The key isn't a property of the row, or the reason is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="coverage"/> is no <see cref="CascadeCoverage"/>.</exception>
    public CacheableListQueryBuilder<TEntity, TResource> CascadeCovered<TDependent>(
        Expression<Func<TDependent, object?>> foreignKey, CascadeCoverage coverage, string reason)
        where TDependent : class
    {
        CascadeDeclarations.Add(CascadeDeclaration.Of(foreignKey, coverage, reason));
        return this;
    }

    /// <summary>
    /// How long an entry lives: the backstop for a missed invalidation, not the mechanism.
    /// </summary>
    /// <param name="lifetime">The lifetime. Ten minutes unless declared.</param>
    /// <returns>The builder.</returns>
    public CacheableListQueryBuilder<TEntity, TResource> Lifetime(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        CachedFor = lifetime;
        return this;
    }

    /// <summary>
    /// What a change to one of its rows does when the service cache can't be told of it before it commits.
    /// </summary>
    /// <param name="writes"><see cref="CacheWrites.Refuse"/> unless declared.</param>
    /// <returns>The builder.</returns>
    public CacheableListQueryBuilder<TEntity, TResource> WhenCacheUnreachable(CacheWrites writes)
    {
        if (!Enum.IsDefined(writes))
        {
            throw new ArgumentOutOfRangeException(nameof(writes), writes, null);
        }

        WhenUnreachable = writes;
        return this;
    }

    private CacheableListQueryBuilder<TEntity, TResource> Keyed(LambdaExpression key, Owner party)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!Enum.IsDefined(party))
        {
            throw new ArgumentOutOfRangeException(nameof(party), party, null);
        }

        Key = key;
        Party = party;
        return this;
    }
}
