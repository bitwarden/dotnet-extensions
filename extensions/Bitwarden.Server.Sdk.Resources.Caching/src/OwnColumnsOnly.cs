using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Refuses a cached read-many whose rows or order read anything but its own table's columns.
/// </summary>
/// <remarks>
/// Only a change to a row of its own table invalidates a read-many's entry, so what decides which rows it holds, and in
/// what order, must be those rows' own columns: a navigation, a subquery or another table would let a change elsewhere
/// leave the entry stale. Values the declaration names (constants, enumeration members) are not rows and are allowed.
/// </remarks>
internal static class OwnColumnsOnly
{
    private static readonly HashSet<string> Orderings = new(StringComparer.Ordinal)
    {
        nameof(Queryable.OrderBy), nameof(Queryable.OrderByDescending), nameof(Queryable.ThenBy), nameof(Queryable.ThenByDescending),
    };

    /// <summary>
    /// Refuses <paramref name="rows"/> or <paramref name="order"/> reaching beyond <typeparamref name="TEntity"/>'s own
    /// columns, and an order that does anything but order.
    /// </summary>
    /// <param name="query">The declaring query, named in a refusal.</param>
    /// <param name="rows">Which rows the read-many holds.</param>
    /// <param name="order">Its order.</param>
    /// <exception cref="InvalidOperationException">Either reads beyond the table's own columns.</exception>
    public static void Require<TEntity>(
        Type query,
        Expression<Func<TEntity, bool>> rows,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order)
        where TEntity : class
    {
        Columns<TEntity>(query, "selects its rows (Where) by", rows);

        var empty = Enumerable.Empty<TEntity>().AsQueryable();
        var step = order(empty).Expression;

        while (step is MethodCallExpression call)
        {
            if (call.Method.DeclaringType != typeof(Queryable) || !Orderings.Contains(call.Method.Name)
                || call.Arguments is not [var source, var key])
            {
                throw new InvalidOperationException(
                    $"{query.Name}'s read-many order (OrderBy) calls {call.Method.Name}. Its order only orders its rows "
                    + "(OrderBy, ThenBy and their descending forms): which rows it holds is its Where alone.");
            }

            Columns<TEntity>(query, "orders its rows (OrderBy) by", Unquoted(key));
            step = source;
        }

        if (step is not ConstantExpression { Value: var root } || !ReferenceEquals(root, empty))
        {
            throw new InvalidOperationException(
                $"{query.Name}'s read-many order (OrderBy) reads rows other than its own. Its order only orders the rows "
                + "it is given.");
        }
    }

    private static LambdaExpression Unquoted(Expression argument) =>
        argument is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted }
            ? quoted
            : (LambdaExpression)argument;

    private static void Columns<TEntity>(Type query, string doing, LambdaExpression lambda) =>
        new ColumnVisitor(query, typeof(TEntity), lambda.Parameters[0], doing).Visit(lambda.Body);

    private sealed class ColumnVisitor(Type query, Type entity, ParameterExpression row, string doing) : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == row)
            {
                if (node.Member is not PropertyInfo property || !Scalar(property.PropertyType))
                {
                    throw Refusal($"{entity.Name}.{node.Member.Name}, which is no column of its own");
                }

                return node;
            }

            // A query the declaration captured is another table's rows, however it is reached.
            return typeof(IQueryable).IsAssignableFrom(node.Type) ? throw Refusal("rows of another query") : base.VisitMember(node);
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == row ? throw Refusal($"the whole {entity.Name} row") : base.VisitParameter(node);

        protected override Expression VisitConstant(ConstantExpression node) =>
            node.Value is IQueryable ? throw Refusal("rows of another query") : base.VisitConstant(node);

        protected override Expression VisitExtension(Expression node) =>
            throw Refusal($"{node.GetType().Name}, which is no column of its own");

        private InvalidOperationException Refusal(string what) => new(
            $"{query.Name} {doing} {what}. A cached read-many reads only its own table's columns: only a change to one "
            + "of its rows invalidates its entry.");

        // A column's type: a value, a string or bytes, never an entity or a collection of them.
        private static bool Scalar(Type type) =>
            type == typeof(string) || type == typeof(byte[])
            || (!typeof(IEnumerable).IsAssignableFrom(type) && (type.IsValueType || type.IsEnum));
    }
}
