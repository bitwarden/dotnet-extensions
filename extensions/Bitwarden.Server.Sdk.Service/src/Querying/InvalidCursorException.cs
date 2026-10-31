using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A cursor the service did not issue, or issued for a different question.
/// </summary>
/// <param name="value">What the caller sent.</param>
/// <param name="reason">Why it cannot be used, as a sentence.</param>
public sealed class InvalidCursorException(string value, string reason)
    : Exception($"'page[after]' cannot be used: {reason}"), IProvideErrors
{
    /// <summary>
    /// The parameter a cursor arrives in.
    /// </summary>
    public const string Parameter = "page[after]";

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors =>
    [
        new()
        {
            Status = "422",
            Code = ErrorCodes.InvalidValue,
            Title = ErrorTitles.InvalidValue,
            Detail = $"'{Parameter}' is not a cursor this operation can continue from: {reason} "
                + $"Start again without it. ('{Truncate(value)}')",
            Source = new ErrorSource { Parameter = Parameter },
        },
    ];

    private static string Truncate(string value) => value.Length <= 40 ? value : $"{value[..40]}…";
}
