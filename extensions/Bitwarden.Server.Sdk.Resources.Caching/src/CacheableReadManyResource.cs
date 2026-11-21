using System.Linq.Expressions;
using System.Reflection;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A declared cached read-many.
/// </summary>
/// <remarks>
/// Its entry is the party's rows, not one row, so no row's version speaks for it: every change to a row of its table,
/// for the party the row named before and the one it names after, whatever the row's state, marks every version of the
/// entry dead for the commit allowance, as a row arriving under a key several rows may hold does. A fill read before
/// the change committed arrives within the allowance and is refused; a read after it sees the change.
/// </remarks>
internal sealed class CacheableReadManyResource<TEntity, TResource> : ICacheableResource, IUniquelyOrdered
    where TEntity : class
    where TResource : Resource
{
    /// <summary>
    /// The version every fill writes: a read-many is versioned by its tombstones alone, each of which marks every
    /// version dead.
    /// </summary>
    public const long FilledVersion = 0;

    private readonly LambdaExpression _key;

    public CacheableReadManyResource(Type query, CacheableReadManyBuilder<TEntity, TResource> declared)
    {
        Query = query;
        ResourceType = ResourceTypes.For(typeof(TResource));

        string Missing(string what) =>
            $"{query.Name} declares a cached read-many of {typeof(TResource).Name} without {what}. A cached read-many "
            + "declares every part of itself.";

        _key = declared.Key ?? throw new InvalidOperationException(Missing("whose rows it holds (KeyedBy)"));
        Rows = declared.Rows ?? throw new InvalidOperationException(Missing("which rows it holds (Where)"));
        Order = declared.Order ?? throw new InvalidOperationException(Missing("its order (OrderBy)"));
        Map = declared.MapToResource ?? throw new InvalidOperationException(Missing("its map (Map)"));
        var scope = declared.ReadScope ?? throw new InvalidOperationException(Missing("the scope it is read with (Scope)"));

        KeyProperty = _key.Body is MemberExpression { Member: PropertyInfo keyed } && keyed.DeclaringType!.IsAssignableFrom(typeof(TEntity))
            ? keyed.Name
            : throw new InvalidOperationException(
                $"{query.Name} keys its read-many by {_key.Body}, which is not a property of {typeof(TEntity).Name}.");
        OrderedLast = LastOrdered(query, Order);
        OwnColumnsOnly.Require(query, Rows, Order);
        ReadMany = new CachedReadMany(declared.Party!.Value, scope, declared.Most);
        Lifetime = declared.CachedFor;
        Cascades = [.. declared.CascadeDeclarations];
        WhenUnreachable = declared.WhenUnreachable;
        Fields = [.. Fields<TResource>.Attributes.Order(StringComparer.Ordinal)];
    }

    public CachedReadMany? ReadMany { get; }

    public Expression<Func<TEntity, bool>> Rows { get; }

    public Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> Order { get; }

    /// <summary>
    /// The property the order ends in, which must be unique for a page of the entry to be the page the database gives.
    /// </summary>
    public string OrderedLast { get; }

    public Func<TEntity, TResource> Map { get; }

    public string ResourceType { get; }

    public Type Resource => typeof(TResource);

    public Type Entity => typeof(TEntity);

    public bool ById => false;

    /// <summary>
    /// The party the key is, as a caller's context names it.
    /// </summary>
    public string KeyName => CacheFill.Axis(ReadMany!.Party);

    public IReadOnlyList<string> Fields { get; }

    public IReadOnlySet<string> PerCaller { get; } = new HashSet<string>(StringComparer.Ordinal);

    public Type Query { get; }

    public TimeSpan Lifetime { get; }

    public CacheWrites WhenUnreachable { get; }

    public IReadOnlyList<CascadeDeclaration> Cascades { get; }

    /// <summary>
    /// The rows are owned (by organizations, for memberships), but the entry is the party's: checked against it.
    /// </summary>
    public ReadOwnership Ownership => ReadOwnership.Rows;

    public Owner? Owner => ReadMany!.Party;

    public bool AcrossOwners => false;

    public IReadOnlyList<string> KeyColumns => [KeyProperty];

    public bool WithinOwner => false;

    public bool NotUnique => false;

    public Owner? CurrentFrom => ReadMany!.Party;

    /// <summary>
    /// None: a read-many is versioned by its tombstones alone.
    /// </summary>
    public string? VersionProperty => null;

    public string KeyProperty { get; }

    public bool KeyIsOwner => false;

    public string Template(string service) =>
        PublishedTemplate(service, current: true)
            .Replace("{" + KeyName + "}", OwnedCacheEntry.KeyPlaceholder, StringComparison.Ordinal);

    public string PublishedTemplate(string service, bool current) =>
        OwnedCacheEntry.ReadManyTemplate(service, ResourceType, KeyName);

    public string? EntryKey(string service, Func<string, object?> column) =>
        column(KeyProperty) is Guid key ? EntryKey(service, key) : null;

    public string EntryKey(string service, Guid key) => OwnedCacheEntry.Key(Template(service), key);

    public IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.INavigation> Included(DbContext context) => [];

    public (string? Current, string? Original) EntryKeysOf(string service, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        (EntryKey(service, column => entry.Property(column).CurrentValue),
            EntryKey(service, column => entry.Property(column).OriginalValue));

    /// <summary>
    /// The party's rows it holds, ordered: every row under the key that <see cref="Rows"/> selects.
    /// </summary>
    public IQueryable<TEntity> Under(IQueryable<TEntity> rows, Guid key)
    {
        var row = _key.Parameters[0];
        var match = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(_key.Body, Expression.Constant(key, _key.Body.Type)),
            row);

        return Order(rows.Where(match).Where(Rows));
    }

    /// <summary>
    /// Refuses an order that doesn't end in a unique property of the table: without one, two reads could give rows that
    /// tie in different orders, and a page of the entry wouldn't be the page the database gives.
    /// </summary>
    /// <param name="context">The service's database.</param>
    /// <exception cref="InvalidOperationException">The order's last key is no unique property.</exception>
    public void RequireUniqueOrder(DbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{Query.Name} reads {typeof(TEntity).Name}, which the database doesn't map.");
        var property = entity.FindProperty(OrderedLast);

        var unique = property is not null
            && (entity.FindPrimaryKey()?.Properties is [var primary] && primary == property
                || entity.GetIndexes().Any(index => index.IsUnique && index.Properties is [var indexed] && indexed == property)
                || entity.GetKeys().Any(key => key.Properties is [var alternate] && alternate == property));

        if (!unique)
        {
            throw new InvalidOperationException(
                $"{Query.Name} orders its read-many last by {typeof(TEntity).Name}.{OrderedLast}, which isn't unique. Its "
                + "order ends in a unique column, so rows that tie are always read in the same order.");
        }
    }

    // The property the last ordering call keys by, from the order applied to an empty query: OrderBy(row => row.Email)
    // .ThenBy(row => row.Id) ends in Id.
    private static string LastOrdered(Type query, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order)
    {
        var expression = order(Enumerable.Empty<TEntity>().AsQueryable()).Expression;

        if (expression is MethodCallExpression { Arguments: [_, var argument] } call
            && call.Method.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending"
            && (argument is UnaryExpression { Operand: LambdaExpression quoted } ? quoted : argument as LambdaExpression) is { } lambda
            && (lambda.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : lambda.Body)
                is MemberExpression { Member: PropertyInfo property })
        {
            return property.Name;
        }

        throw new InvalidOperationException(
            $"{query.Name} orders its read-many by something other than a property of {typeof(TEntity).Name} last. Its "
            + "order ends in a unique column (OrderBy(rows => rows.OrderBy(row => row.Name).ThenBy(row => row.Id))).");
    }
}
