using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A sort the operation cannot apply.
/// </summary>
/// <param name="value">What the caller sent.</param>
/// <param name="problem">Why it cannot be applied.</param>
/// <param name="allowed">The published names of the fields the collection does sort by.</param>
/// <remarks>
/// The detail lists what the caller could have asked for. A sort is rejected because the caller
/// guessed at a field name, and a rejection that does not say which names exist leaves them
/// guessing again.
/// </remarks>
public sealed class InvalidSortException(string value, string problem, IReadOnlyList<string> allowed)
    : Exception($"'{Sort.Parameter}' cannot be applied: {problem}."), IProvideErrors
{
    /// <inheritdoc />
    public IReadOnlyList<Error> Errors =>
    [
        new()
        {
            Status = "422",
            Code = ErrorCodes.InvalidValue,
            Title = ErrorTitles.InvalidValue,
            Detail = $"'{value}' cannot be used to sort: {problem}. {Offer()}",
            Source = new ErrorSource { Parameter = Sort.Parameter },
        },
    ];

    private string Offer() =>
        allowed.Count == 0
            ? "This collection cannot be sorted."
            : $"Sortable fields are {string.Join(", ", allowed.Select(name => $"'{name}'"))}.";
}
