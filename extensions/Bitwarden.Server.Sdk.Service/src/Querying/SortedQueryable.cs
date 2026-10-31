using System.Linq.Expressions;
using System.Reflection;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Applies a <see cref="Sort"/> to a query.
/// </summary>
/// <remarks>
/// A convenience, not the only way. A query that cannot express its ordering as columns
/// (because a field is computed, or lives on a joined row) reads <see cref="Sort.Fields"/> and
/// orders itself. What is not optional is that the order applied is the order the caller asked
/// for, which the binder has already validated by the time a query sees it.
/// </remarks>
public static class SortedQueryable
{
    /// <summary>
    /// Orders a query by the fields of a sort, in order of precedence.
    /// </summary>
    /// <typeparam name="TEntity">The row being ordered.</typeparam>
    /// <param name="source">The query to order.</param>
    /// <param name="sort">The order to apply.</param>
    /// <returns>The ordered query.</returns>
    /// <exception cref="InvalidOperationException">
    /// The row has no property matching one of the sort's fields. A wiring error rather than a
    /// caller's: the field was published as sortable, so a request naming it has already been
    /// accepted.
    /// </exception>
    public static IOrderedQueryable<TEntity> SortedBy<TEntity>(this IQueryable<TEntity> source, Sort sort)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sort);

        IOrderedQueryable<TEntity>? ordered = null;

        // Resolved all at once, the first time this resource is sorted against this entity, so a
        // sortable field with nowhere to go fails the first query rather than the request naming it.
        var stored = sort.Resource is { } resource ? StoredFields.SortableOn(resource, typeof(TEntity)) : null;

        foreach (var by in sort.Fields)
        {
            ordered = Then(source, ordered, by, Property<TEntity>(by, stored));
        }

        return ordered ?? throw new InvalidOperationException("A sort must order by at least one field.");
    }

    /// <summary>
    /// Orders a query by the fields of a sort, then by a field that breaks ties.
    /// </summary>
    /// <typeparam name="TEntity">The row being ordered.</typeparam>
    /// <typeparam name="TKey">What the tie is broken on.</typeparam>
    /// <param name="source">The query to order.</param>
    /// <param name="sort">The order to apply.</param>
    /// <param name="tieBreak">A field that is unique per row, such as its key.</param>
    /// <returns>The ordered query.</returns>
    /// <remarks>
    /// Worth reaching for whenever the result is paged. Sorting by a column that repeats leaves
    /// the order of the rows sharing a value up to the database, and nothing obliges it to
    /// choose the same order twice, so a row can appear on two consecutive pages, or on
    /// neither, while nothing about the data changed. A unique final field removes the choice.
    /// </remarks>
    public static IOrderedQueryable<TEntity> SortedBy<TEntity, TKey>(
        this IQueryable<TEntity> source,
        Sort sort,
        Expression<Func<TEntity, TKey>> tieBreak)
    {
        ArgumentNullException.ThrowIfNull(tieBreak);

        return source.SortedBy(sort).ThenBy(tieBreak);
    }

    /// <remarks>
    /// Through the resource's declarations when the sort carries its resource (a field stored under
    /// another name says <c>MappedTo</c>), and by name only for a sort built by hand.
    /// </remarks>
    private static PropertyInfo Property<TEntity>(SortBy by, IReadOnlyDictionary<string, PropertyInfo>? stored) =>
        stored is not null
            ? stored.TryGetValue(by.Field, out var declared)
                ? declared
                : throw new InvalidOperationException(
                    $"'{by.Field}' is not a sortable field of the resource this sort was made for.")
            : StoredFields.Named(typeof(TEntity), by.Field)
              ?? throw new InvalidOperationException(
                  $"'{typeof(TEntity).Name}' has no property '{by.Field}' to sort by. A sort built by "
                  + "hand names the entity's own properties.");

    private static IOrderedQueryable<TEntity> Then<TEntity>(
        IQueryable<TEntity> source,
        IOrderedQueryable<TEntity>? ordered,
        SortBy by,
        PropertyInfo property)
    {

        var parameter = Expression.Parameter(typeof(TEntity), "entity");

        var selector = Expression.Lambda(Expression.Property(parameter, property), parameter);

        var method = (ordered, by.IsDescending) switch
        {
            (null, false) => nameof(Queryable.OrderBy),
            (null, true) => nameof(Queryable.OrderByDescending),
            (_, false) => nameof(Queryable.ThenBy),
            (_, true) => nameof(Queryable.ThenByDescending),
        };

        var call = Expression.Call(
            typeof(Queryable),
            method,
            [typeof(TEntity), property.PropertyType],
            (ordered ?? source).Expression,
            Expression.Quote(selector));

        return (IOrderedQueryable<TEntity>)source.Provider.CreateQuery<TEntity>(call);
    }
}
