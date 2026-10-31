using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// Turns a <see cref="Query"/> into a predicate a database can run.
/// </summary>
/// <remarks>
/// <para>
/// Compiled to an expression tree rather than evaluated in memory, so the filter reaches the
/// database and a bulk operation over a large table does not load it first.
/// </para>
/// <para>
/// A convenience, not the only way: the peer of <see cref="SortedQueryable"/>. A query whose
/// fields are computed, or live on a joined row, reads the expression itself. What is not
/// optional is that a field the resource does not declare has already been refused before a
/// query sees one.
/// </para>
/// </remarks>
public static class QueryableQuery
{
    /// <summary>
    /// Narrows a query to the rows an expression matches.
    /// </summary>
    /// <typeparam name="TEntity">The row being filtered.</typeparam>
    /// <param name="source">The query to narrow.</param>
    /// <param name="query">The expression to apply.</param>
    /// <returns>The narrowed query.</returns>
    /// <exception cref="InvalidOperationException">
    /// The row has no property matching a field the expression names. A wiring error rather
    /// than a caller's: the field is declared on the resource, so a request naming it has
    /// already been accepted. Or the expression was read from a request and never checked
    /// against its resource, which happens only when it reaches a handler some way other than
    /// a <see cref="Search{T}"/> or bulk request on the operation that takes it.
    /// </exception>
    /// <exception cref="InvalidRequestException">
    /// The expression compares a field in a way its type does not allow: orders one that has no
    /// order, asks for null where it cannot be null, or looks for a substring in something other
    /// than text.
    /// </exception>
    public static IQueryable<TEntity> Matching<TEntity>(this IQueryable<TEntity> source, Query query)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        // Refused rather than matched by the entity's own property names, which would let a
        // caller filter on, and probe with 'in', columns the resource never publishes.
        if (query is { ReadFromText: true, Resource: null })
        {
            throw new InvalidOperationException(
                "This expression was read from a request but never checked against the fields its "
                + "resource marks filterable. Take it as a Search<T> on a [StandardSearch] operation, or as "
                + "a bulk request on a [StandardBulkUpdate], [StandardBulkDelete] or [StandardBulkAction] one.");
        }

        var parameter = Expression.Parameter(typeof(TEntity), "row");

        // Resolved all at once, the first time this resource is matched against this entity, so a
        // filterable field with nowhere to go fails the first query rather than the request naming it.
        var stored = query.Resource is { } resource ? StoredFields.FilterableOn(resource, typeof(TEntity)) : null;
        var row = new Row(parameter, stored);

        return source.Where(Expression.Lambda<Func<TEntity, bool>>(Predicate(query.Root, row), parameter));
    }

    private static Expression Predicate(QueryTerm term, Row row) => term.Operator switch
    {
        QueryOperator.And => Combine(term, row, Expression.AndAlso),
        QueryOperator.Or => Combine(term, row, Expression.OrElse),
        QueryOperator.Not => Expression.Not(Predicate(Single(term), row)),
        QueryOperator.Equal => Compare(term, row, Expression.Equal),
        QueryOperator.NotEqual => Compare(term, row, Expression.NotEqual),
        QueryOperator.GreaterThan => Compare(term, row, Expression.GreaterThan),
        QueryOperator.GreaterThanOrEqual => Compare(term, row, Expression.GreaterThanOrEqual),
        QueryOperator.LessThan => Compare(term, row, Expression.LessThan),
        QueryOperator.LessThanOrEqual => Compare(term, row, Expression.LessThanOrEqual),
        QueryOperator.In => Contains(term, row),
        _ => throw new InvalidOperationException(
            $"'{term.Operator}' is not something a query can be narrowed by on its own."),
    };

    private static Expression Combine(
        QueryTerm term,
        Row row,
        Func<Expression, Expression, Expression> join)
    {
        if (term.Terms.Count < 2)
        {
            throw new InvalidRequestException(
                $"'{term.Operator}' combines two or more terms, and was given {term.Terms.Count}.",
                "/filter");
        }

        return Balanced([.. term.Terms.Select(each => Predicate(each, row))], join);
    }

    /// <summary>
    /// The terms joined as a balanced tree rather than a left fold, so the expression is as deep as
    /// the logarithm of their number, not the number: every query provider walks it recursively.
    /// </summary>
    private static Expression Balanced(ReadOnlySpan<Expression> terms, Func<Expression, Expression, Expression> join) =>
        terms.Length == 1
            ? terms[0]
            : join(Balanced(terms[..(terms.Length / 2)], join), Balanced(terms[(terms.Length / 2)..], join));

    private static QueryTerm Single(QueryTerm term) =>
        term.Terms.Count == 1
            ? term.Terms[0]
            : throw new InvalidRequestException(
                $"'!' negates exactly one term, and was given {term.Terms.Count}.", "/filter");

    private static Expression Compare(
        QueryTerm term,
        Row row,
        Func<Expression, Expression, Expression> compare)
    {
        var (field, literal) = Pair(term, row);
        var value = Constant(literal, field.Type);

        try
        {
            return compare(field, value);
        }
        catch (InvalidOperationException)
        {
            // Only an ordering can fail here: every type has an equality, and text, a bool or an
            // enumeration has no order a filter could mean.
            throw new InvalidRequestException(
                $"'{term.Terms.First(each => each.Operator == QueryOperator.Var).Field}' cannot be ordered, so "
                + "it can be compared only with '==', '!=' or 'in'.",
                "/filter");
        }
    }

    /// <remarks>
    /// Two readings, and the standards give both: membership in a list, and a substring match.
    /// Which one is meant is settled by the shape of the operand rather than by a second
    /// operator: an array is a list to be in, and a string is text to be found in.
    /// </remarks>
    private static Expression Contains(QueryTerm term, Row row)
    {
        var (field, literal) = Pair(term, row);

        if (literal.ValueKind == JsonValueKind.Array)
        {
            // Of the field's own type, so a field that can be null can be asked for null.
            var values = literal.EnumerateArray()
                .Select(value => Convert(value, field.Type))
                .ToArray();

            var typed = Array.CreateInstance(field.Type, values.Length);
            Array.Copy(values, typed, values.Length);

            return Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                [field.Type],
                Expression.Constant(typed),
                field);
        }

        if (field.Type != typeof(string) || literal.ValueKind != JsonValueKind.String)
        {
            throw new InvalidRequestException(
                "'in' looks for text in a text field, or for a field's value in a list.", "/filter");
        }

        return Expression.Call(
            field,
            typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
            Expression.Constant(literal.GetString(), typeof(string)));
    }

    /// <remarks>
    /// Every comparison is a field against a literal. Comparing two fields, or two literals, is
    /// expressible in the notation and means nothing useful in a filter, so it is refused
    /// rather than half-supported.
    /// </remarks>
    private static (MemberExpression Field, JsonElement Literal) Pair(
        QueryTerm term,
        Row row)
    {
        if (term.Terms.Count != 2)
        {
            throw new InvalidRequestException(
                $"'{term.Operator}' compares exactly two operands, and was given {term.Terms.Count}.",
                "/filter");
        }

        var named = term.Terms.Where(each => each.Operator == QueryOperator.Var).ToArray();
        var literals = term.Terms.Where(each => each.Value is not null).ToArray();

        if (named.Length != 1 || literals.Length != 1)
        {
            throw new InvalidRequestException(
                "A comparison must be between one field and one value.", "/filter");
        }

        return (Member(row, named[0].Field!), literals[0].Value!.Value);
    }

    /// <remarks>
    /// Through the resource's declarations when the query was validated against one (a field stored
    /// under another name says <c>MappedTo</c>), and by name only for a query built by hand.
    /// </remarks>
    private static MemberExpression Member(Row row, string field)
    {
        var property = row.Stored is not null
            ? row.Stored.TryGetValue(field, out var declared)
                ? declared
                : throw new InvalidOperationException(
                    $"'{field}' is not a filterable field of the resource this query was validated for.")
            : StoredFields.Named(row.Parameter.Type, field)
              ?? throw new InvalidOperationException(
                  $"{row.Parameter.Type.Name} has no property '{field}' to filter on. A query built by "
                  + "hand names the entity's own properties.");

        return Expression.Property(row.Parameter, property);
    }

    /// <summary>
    /// The row being filtered, and where each field it may be filtered by is stored.
    /// </summary>
    private sealed record Row(ParameterExpression Parameter, IReadOnlyDictionary<string, PropertyInfo>? Stored);

    private static Expression Constant(JsonElement literal, Type type) =>
        Expression.Constant(Convert(literal, type), type);

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static object? Convert(JsonElement literal, Type type)
    {
        var target = Underlying(type);

        if (literal.ValueKind is JsonValueKind.Null)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null
                ? null
                : throw new InvalidRequestException(
                    "null is not a value this field can hold, so nothing can be compared against it.", "/filter");
        }

        try
        {
            if (target.IsEnum)
            {
                // By published name, as everything else about an enumeration on the wire is.
                return EnumValues.TryParse(target, literal.GetString() ?? string.Empty, out var member)
                    ? member
                    : throw new FormatException();
            }

            return literal.Deserialize(target, JsonSerializerOptions.Web);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            throw new InvalidRequestException(
                $"{literal} is not a value this field can be compared against.", "/filter");
        }
    }
}
