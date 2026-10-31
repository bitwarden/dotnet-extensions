using System.Linq.Expressions;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Narrows a query to the rows whose field falls within a range.
/// </summary>
/// <remarks>
/// The peer of <see cref="SortedQueryable"/>: compiled to an expression tree so the comparison
/// reaches the database, and told which field the range applies to rather than guessing it from
/// the filter's name: the operation decides what a filter means.
/// </remarks>
public static class RangeQueryable
{
    /// <summary>
    /// Narrows a query to the rows whose field falls within the range.
    /// </summary>
    /// <typeparam name="TEntity">The row being filtered.</typeparam>
    /// <typeparam name="T">What the range spans.</typeparam>
    /// <param name="source">The query to narrow.</param>
    /// <param name="field">The field the range applies to.</param>
    /// <param name="range">The range, or null to leave the query as it is.</param>
    /// <returns>The narrowed query.</returns>
    public static IQueryable<TEntity> Within<TEntity, T>(
        this IQueryable<TEntity> source,
        Expression<Func<TEntity, T>> field,
        Range<T>? range)
        where T : struct, IComparable<T>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(field);

        if (range is null)
        {
            return source;
        }

        if (range.Lower is { } lower)
        {
            source = source.Where(Compare(field, lower, range.LowerInclusive ? ExpressionType.GreaterThanOrEqual : ExpressionType.GreaterThan));
        }

        if (range.Upper is { } upper)
        {
            source = source.Where(Compare(field, upper, range.UpperInclusive ? ExpressionType.LessThanOrEqual : ExpressionType.LessThan));
        }

        return source;
    }

    /// <remarks>
    /// The bound is a closure over a variable rather than a constant, so the provider sends it as a
    /// parameter: one cached query plan however many ranges are asked for, and no value spliced
    /// into SQL text.
    /// </remarks>
    private static Expression<Func<TEntity, bool>> Compare<TEntity, T>(
        Expression<Func<TEntity, T>> field,
        T bound,
        ExpressionType comparison)
    {
        Expression<Func<T>> captured = () => bound;

        return Expression.Lambda<Func<TEntity, bool>>(
            Expression.MakeBinary(comparison, field.Body, captured.Body),
            field.Parameters);
    }
}
