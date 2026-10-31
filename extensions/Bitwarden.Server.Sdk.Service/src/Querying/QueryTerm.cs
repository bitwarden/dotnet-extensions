using System.Text.Json;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// One node of a query expression.
/// </summary>
/// <remarks>
/// A term is either a field reference, a literal, or an operator applied to other terms.
/// Building this rather than evaluating the JSON directly is what lets the expression be
/// validated once, completely, before anything tries to run it, which is the difference
/// between a caller getting one <c>422</c> naming the problem and a caller getting a <c>500</c>
/// from somewhere inside a query provider.
/// </remarks>
public sealed record QueryTerm
{
    /// <summary>
    /// What this term does, or null when it is a literal.
    /// </summary>
    public QueryOperator? Operator { get; init; }

    /// <summary>
    /// The field this term references, when it is a <see cref="QueryOperator.Var"/>.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// The literal value, when the term is one.
    /// </summary>
    public JsonElement? Value { get; init; }

    /// <summary>
    /// The terms this operator applies to.
    /// </summary>
    public IReadOnlyList<QueryTerm> Terms { get; init; } = [];
}
