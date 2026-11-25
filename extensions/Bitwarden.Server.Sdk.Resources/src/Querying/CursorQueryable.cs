using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Bitwarden.Server.Sdk.Resources.Querying;

/// <summary>
/// Pages a query by position: everything after the last row a caller saw, in the caller's order.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, because the platform does not know how a service reads its database. <see cref="Page"/>
/// narrows and orders the query and takes one row more than the page holds; the service
/// materialises it however it materialises anything; <see cref="ToCursorPage"/> keeps the page,
/// and if the extra row was there, mints the cursor from the last row kept:
/// </para>
/// <code>
/// var rows = await database.Events
///     .Page(page, sort ?? Sort.Default&lt;Event&gt;(), entity => entity.Id)
///     .ToListAsync(cancellationToken);
///
/// return rows.ToCursorPage(page, sort ?? Sort.Default&lt;Event&gt;(), entity => entity.Id, ToResource);
/// </code>
/// <para>
/// The tie-break must be unique (the row's identifier) because it is what makes "after this
/// row" mean exactly one place when several rows share a sort value. Without it, rows sharing a
/// timestamp at a page boundary are skipped.
/// </para>
/// <para>
/// A sort field that can be null, a <see cref="Nullable{T}"/> or a reference type annotated as
/// nullable, is refused. Providers disagree about where nulls sort, so a cursor through one would
/// mean different things on SQL Server and on PostgreSQL, and the data layer must mean the same
/// thing on both.
/// </para>
/// <para>
/// A cursor records the sort it was minted under, and is refused under any other. It does not
/// record the filters: a caller who changes them mid-walk gets the continuation of a different
/// question (harmless, since it is still only their own rows) and should start again.
/// </para>
/// </remarks>
public static class CursorQueryable
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerOptions.Web) { AllowDuplicateProperties = false };

    /// <summary>
    /// Narrows a query to what follows the cursor, orders it, and takes one more row than the page holds.
    /// </summary>
    /// <typeparam name="TEntity">The row being paged.</typeparam>
    /// <typeparam name="TKey">The tie-break's type.</typeparam>
    /// <param name="source">The query, already filtered.</param>
    /// <param name="page">The page asked for.</param>
    /// <param name="sort">The order to page in.</param>
    /// <param name="tieBreak">A unique field ordering rows that share every sort value: the identifier.</param>
    /// <returns>The query to materialise.</returns>
    /// <exception cref="InvalidCursorException">The cursor is malformed, or was minted under a different sort.</exception>
    public static IQueryable<TEntity> Page<TEntity, TKey>(
        this IQueryable<TEntity> source,
        CursorPage page,
        Sort sort,
        Expression<Func<TEntity, TKey>> tieBreak)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(tieBreak);

        var keys = Keys(sort, tieBreak);

        if (page.After is { } cursor)
        {
            source = source.Where(After<TEntity>(keys, tieBreak.Parameters[0], Decode(cursor, sort, keys)));
        }

        return source.SortedBy(sort, tieBreak).Take(page.Size + 1);
    }

    /// <summary>
    /// Keeps the page from what <see cref="Page"/> read, and mints the cursor for the next one.
    /// </summary>
    /// <typeparam name="TEntity">The row being paged.</typeparam>
    /// <typeparam name="TKey">The tie-break's type.</typeparam>
    /// <typeparam name="TResource">What the page publishes.</typeparam>
    /// <param name="rows">The rows <see cref="Page"/>'s query returned.</param>
    /// <param name="page">The page asked for.</param>
    /// <param name="sort">The order paged in: the same one given to <see cref="Page"/>.</param>
    /// <param name="tieBreak">The tie-break given to <see cref="Page"/>.</param>
    /// <param name="map">Turns a row into what the page publishes.</param>
    /// <returns>The page, with a cursor when more follow.</returns>
    public static CursorPaged<TResource> ToCursorPage<TEntity, TKey, TResource>(
        this IReadOnlyList<TEntity> rows,
        CursorPage page,
        Sort sort,
        Expression<Func<TEntity, TKey>> tieBreak,
        Func<TEntity, TResource> map)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(tieBreak);
        ArgumentNullException.ThrowIfNull(map);

        var kept = rows.Take(page.Size).ToList();
        var cursor = rows.Count > page.Size ? Encode(sort, Keys(sort, tieBreak), kept[^1]!) : null;

        return new CursorPaged<TResource>([.. kept.Select(map)], cursor);
    }

    /// <summary>
    /// One column of the order: how to read it from a row, and which way it runs.
    /// </summary>
    private sealed record Key(LambdaExpression Selector, Type Type, bool Descending)
    {
        public object? Read(object row) => Selector.Compile().DynamicInvoke(row);
    }

    private static List<Key> Keys<TEntity, TKey>(Sort sort, Expression<Func<TEntity, TKey>> tieBreak)
    {
        var keys = new List<Key>(sort.Fields.Count + 1);
        var row = tieBreak.Parameters[0];
        var stored = sort.Resource is { } resource ? StoredFields.SortableOn(resource, typeof(TEntity)) : null;

        foreach (var by in sort.Fields)
        {
            var property = StoredFields.Property(
                stored,
                typeof(TEntity),
                by.Field,
                () => $"'{by.Field}' is not a sortable field of the resource this sort was made for.",
                () => $"'{typeof(TEntity).Name}' has no property '{by.Field}' to page by. A sort built "
                    + "by hand names the entity's own properties.");

            RequireNotNullable(property, $"{typeof(TEntity).Name}.{property.Name}");

            keys.Add(new Key(
                Expression.Lambda(Expression.Property(row, property), row),
                property.PropertyType,
                by.IsDescending));
        }

        if (tieBreak.Body is MemberExpression { Member: PropertyInfo tieBreakProperty })
        {
            RequireNotNullable(tieBreakProperty, $"the tie-break of {typeof(TEntity).Name}");
        }
        else
        {
            RequireNotNullable(typeof(TKey), $"the tie-break of {typeof(TEntity).Name}");
        }

        // Ascending, as SortedBy applies it. Which way does not matter; agreeing does.
        keys.Add(new Key(tieBreak, typeof(TKey), Descending: false));

        return keys;
    }

    /// <remarks>
    /// A reference type annotated as nullable (<c>string?</c>) counts as nullable as much as a
    /// <see cref="Nullable{T}"/> does: a row holding null would mint a cursor that names no place,
    /// and a comparison with null matches no row. One whose nullability is not annotated is taken
    /// as declared.
    /// </remarks>
    private static void RequireNotNullable(PropertyInfo property, string what)
    {
        if (!property.PropertyType.IsValueType
            && new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable)
        {
            throw CanBeNull(what);
        }

        RequireNotNullable(property.PropertyType, what);
    }

    private static void RequireNotNullable(Type type, string what)
    {
        if (Nullable.GetUnderlyingType(type) is not null)
        {
            throw CanBeNull(what);
        }
    }

    private static InvalidOperationException CanBeNull(string what) =>
        new($"{what} can be null, and cannot be paged by cursor: providers disagree about where "
            + "nulls sort, so the same cursor would mean different things on each.");

    /// <summary>
    /// The rows strictly after the cursor, in the order the keys describe.
    /// </summary>
    /// <remarks>
    /// The usual lexicographic form (the first key beyond the cursor's, or equal on it and the
    /// second beyond, and so on), each compared in its own direction. Written as the expanded
    /// disjunction rather than a row-value comparison because not every provider has one, and
    /// because the directions may be mixed.
    /// </remarks>
    private static Expression<Func<TEntity, bool>> After<TEntity>(
        List<Key> keys,
        ParameterExpression row,
        object?[] values)
    {
        Expression? any = null;

        for (var i = 0; i < keys.Count; i++)
        {
            Expression all = Beyond(Read(keys[i], row), CapturedValue.Of(values[i], keys[i].Type), keys[i].Descending);

            for (var j = i - 1; j >= 0; j--)
            {
                all = Expression.AndAlso(
                    Expression.Equal(Read(keys[j], row), CapturedValue.Of(values[j], keys[j].Type)),
                    all);
            }

            any = any is null ? all : Expression.OrElse(any, all);
        }

        return Expression.Lambda<Func<TEntity, bool>>(any!, row);
    }

    private static Expression Read(Key key, ParameterExpression row) =>
        new Rebind(key.Selector.Parameters[0], row).Visit(key.Selector.Body);

    /// <remarks>
    /// An operator where the type has one. Where it has none (a <see cref="Guid"/>, a string),
    /// <c>CompareTo</c> against zero, which providers translate to the database's own comparison:
    /// the same comparison its <c>ORDER BY</c> uses, which is the only agreement that matters.
    /// An enumeration compares as the number it is stored as.
    /// </remarks>
    private static Expression Beyond(Expression field, Expression value, bool descending)
    {
        if (field.Type.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(field.Type);
            field = Expression.Convert(field, underlying);
            value = Expression.Convert(value, underlying);
        }

        var comparison = descending ? ExpressionType.LessThan : ExpressionType.GreaterThan;

        try
        {
            return Expression.MakeBinary(comparison, field, value);
        }
        catch (InvalidOperationException)
        {
            var compare = field.Type.GetMethod(nameof(IComparable.CompareTo), [field.Type])
                ?? throw new InvalidOperationException($"{field.Type.Name} cannot be compared, so it cannot be paged by.");

            return Expression.MakeBinary(comparison, Expression.Call(field, compare, value), Expression.Constant(0));
        }
    }

    private static string Encode(Sort sort, List<Key> keys, object last)
    {
        var values = new JsonArray();

        foreach (var key in keys)
        {
            values.Add(JsonSerializer.SerializeToNode(key.Read(last), key.Type, JsonSerializerOptions.Web));
        }

        var json = new JsonObject { ["s"] = sort.ToString(), ["k"] = values }.ToJsonString();

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static object?[] Decode(string cursor, Sort sort, List<Key> keys)
    {
        Issued? decoded;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

            // Read whole and typed, so anything in the wrong place (a sort that is not text, keys
            // that are not a list, text that is not UTF-8 or UTF-16, a property given twice) is
            // refused here, by the reader.
            decoded = JsonSerializer.Deserialize<Issued>(Convert.FromBase64String(base64), Strict);
        }
        // FromBase64String refuses what is not base64, and Deserialize what is not a cursor.
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new InvalidCursorException(cursor, "it was not issued by this operation.");
        }

        if (decoded?.S is not { } issuedFor)
        {
            throw new InvalidCursorException(cursor, "it was not issued by this operation.");
        }

        if (issuedFor != sort.ToString())
        {
            throw new InvalidCursorException(
                cursor,
                "it was issued for a different sort. A cursor continues the order it was issued in.");
        }

        if (decoded.K is not { } values || values.Length != keys.Count)
        {
            throw new InvalidCursorException(cursor, "it was not issued by this operation.");
        }

        var typed = new object?[keys.Count];

        for (var i = 0; i < keys.Count; i++)
        {
            try
            {
                typed[i] = values[i].Deserialize(keys[i].Type, JsonSerializerOptions.Web)
                    ?? throw new InvalidCursorException(cursor, "it was not issued by this operation.");
            }
            // JsonException for a value the key's type cannot hold, NotSupportedException for a key
            // type with no converter: no cursor this operation minted can hold one of those.
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                throw new InvalidCursorException(cursor, "it was not issued by this operation.");
            }
        }

        return typed;
    }

    /// <summary>
    /// A cursor as <see cref="Encode"/> writes it: the sort it was minted under, and the last row's keys.
    /// </summary>
    private sealed class Issued
    {
        [JsonPropertyName("s")]
        public string? S { get; init; }

        [JsonPropertyName("k")]
        public JsonElement[]? K { get; init; }
    }

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}
