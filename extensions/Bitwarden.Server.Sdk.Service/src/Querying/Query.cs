using System.Text.Json;
using System.Text.Json.Serialization;
using Bitwarden.Server.Sdk.Common;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A validated query expression, as sent in a <c>filter</c> field.
/// </summary>
/// <remarks>
/// <para>
/// The standards adopt <see href="https://jsonlogic.com/">JSON Logic</see> for its notation
/// only. Its full operator set includes control flow, iteration and arithmetic, none of which
/// belong in a query, so exactly eleven operators are supported and anything else is refused
/// with a <c>422</c> naming what was used.
/// </para>
/// <para>
/// A field the resource does not declare is refused the same way. That is not politeness: an
/// unrecognised field silently ignored is a filter that matches more than the caller asked
/// for, and on a bulk delete that is the difference between removing four rows and removing
/// the table.
/// </para>
/// </remarks>
[JsonConverter(typeof(QueryJsonConverter))]
public sealed record Query
{
    /// <summary>
    /// The root of the expression.
    /// </summary>
    public required QueryTerm Root { get; init; }

    /// <summary>
    /// The resource the expression was validated against, which is what lets it find where each
    /// field is stored. Set by the platform when it checks a search or bulk request; absent on an
    /// expression built by hand, whose fields are taken as the entity's own property names.
    /// </summary>
    internal Type? Resource { get; set; }

    /// <summary>
    /// Whether the expression was read from text, which is how a caller's arrives, rather than
    /// built from terms in code.
    /// </summary>
    /// <remarks>
    /// One read from text is matched only once it has been checked against its resource: by name
    /// alone it could reach any property of the entity, including those the resource never
    /// publishes.
    /// </remarks>
    internal bool ReadFromText { get; private init; }

    /// <summary>
    /// The most terms an expression may hold, counting every operator, field and literal: a
    /// comparison is three. Far beyond any filter a caller writes, and small enough that the
    /// expression compiled from it, and every query provider's walk over that, stays shallow and
    /// cheap. An <c>in</c> list is one literal, however long.
    /// </summary>
    public const int MaxTerms = 500;

    /// <summary>
    /// The operators the standards permit, by their JSON Logic spelling.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, QueryOperator> Operators =
        new Dictionary<string, QueryOperator>(StringComparer.Ordinal)
        {
            ["var"] = QueryOperator.Var,
            ["=="] = QueryOperator.Equal,
            ["!="] = QueryOperator.NotEqual,
            [">"] = QueryOperator.GreaterThan,
            [">="] = QueryOperator.GreaterThanOrEqual,
            ["<"] = QueryOperator.LessThan,
            ["<="] = QueryOperator.LessThanOrEqual,
            ["in"] = QueryOperator.In,
            ["and"] = QueryOperator.And,
            ["or"] = QueryOperator.Or,
            ["!"] = QueryOperator.Not,
        };

    /// <summary>
    /// Reads an expression, refusing anything the standards do not permit.
    /// </summary>
    /// <param name="element">The <c>filter</c> object.</param>
    /// <param name="pointer">Where the expression sits in the request, for the error to point at.</param>
    /// <returns>The expression.</returns>
    /// <exception cref="InvalidRequestException">
    /// The expression uses an operator outside the permitted set, is malformed, is not a condition
    /// where one belongs, or holds more than <see cref="MaxTerms"/> terms.
    /// </exception>
    /// <remarks>
    /// An expression read this way is taken as a caller's: it is matched against rows only once the
    /// platform has checked it against the resource's filterable fields, as it does for a
    /// <see cref="Search{T}"/> or a bulk request on the operations that take them.
    /// </remarks>
    public static Query Parse(JsonElement element, string pointer = "/filter")
    {
        var terms = 0;
        return new() { Root = Term(element, pointer, pointer, ref terms, condition: true), ReadFromText = true };
    }

    /// <param name="element">The term.</param>
    /// <param name="pointer">Where it sits.</param>
    /// <param name="root">Where the expression sits, for a refusal of the whole of it.</param>
    /// <param name="terms">How many terms have been read so far.</param>
    /// <param name="condition">
    /// Whether the term must hold or not of a row: the root, and each operand of <c>and</c>,
    /// <c>or</c> and <c>!</c>. A literal or a bare field there has no meaning as a filter.
    /// </param>
    private static QueryTerm Term(JsonElement element, string pointer, string root, ref int terms, bool condition)
    {
        // Counted before anything is built, so an expression too large is refused before it costs
        // anything beyond its parse.
        if (++terms > MaxTerms)
        {
            throw Invalid(root, $"An expression may hold at most {MaxTerms} terms, counting every operator, field and literal.");
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return condition
                ? throw NotACondition(pointer)
                : new QueryTerm { Value = element };
        }

        var members = element.EnumerateObject().ToArray();

        if (members.Length != 1)
        {
            throw Invalid(
                pointer,
                members.Length == 0
                    ? "An expression must apply exactly one operator, and this one applies none."
                    : $"An expression must apply exactly one operator, and this one applies {members.Length}: "
                      + $"{string.Join(", ", members.Select(member => $"'{member.Name}'"))}.");
        }

        var (name, argument) = (members[0].Name, members[0].Value);

        if (!Operators.TryGetValue(name, out var found))
        {
            throw Invalid(
                pointer,
                $"'{name}' is not an operator a query may use. The permitted operators are "
                + $"{string.Join(", ", Operators.Keys.Select(key => $"'{key}'"))}.");
        }

        if (found == QueryOperator.Var)
        {
            if (condition)
            {
                throw NotACondition(pointer);
            }

            return new QueryTerm
            {
                Operator = QueryOperator.Var,
                Field = argument.ValueKind == JsonValueKind.String && argument.GetString() is { Length: > 0 } field
                    ? field
                    : throw Invalid(pointer, "'var' must name a field as a string."),
            };
        }

        var combines = found is QueryOperator.And or QueryOperator.Or or QueryOperator.Not;

        var arguments = argument.ValueKind == JsonValueKind.Array
            ? argument.EnumerateArray().ToArray()
            : [argument];

        var operands = new QueryTerm[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            operands[index] = Term(arguments[index], $"{pointer}/{name}/{index}", root, ref terms, condition: combines);
        }

        return new QueryTerm
        {
            Operator = found,
            Terms = operands,
        };
    }

    private static InvalidRequestException NotACondition(string pointer) =>
        Invalid(
            pointer,
            "A filter here must be a condition: a comparison such as '==' or 'in' between a field and a "
            + "value, or 'and', 'or' or '!' over conditions.");

    private static InvalidRequestException Invalid(string pointer, string detail) =>
        new(detail, pointer);
}
