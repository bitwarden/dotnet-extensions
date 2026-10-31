using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A filter value the operation cannot act on.
/// </summary>
public sealed class InvalidFilterException : Exception, IProvideErrors
{
    private readonly string _parameter;
    private readonly string _detail;

    /// <summary>
    /// Describes a value the filter does not accept.
    /// </summary>
    /// <param name="parameter">The parameter's name.</param>
    /// <param name="value">What the caller sent.</param>
    /// <param name="expectation">What the filter accepts, phrased to follow the parameter's name.</param>
    public InvalidFilterException(string parameter, string value, string expectation)
        : this(parameter, $"'{parameter}' {expectation}, not '{value}'.")
    {
    }

    private InvalidFilterException(string parameter, string detail)
        : base(detail)
    {
        _parameter = parameter;
        _detail = detail;
    }

    /// <summary>
    /// Describes a filter the operation cannot act on, in a sentence of its own.
    /// </summary>
    /// <param name="parameter">The parameter's name.</param>
    /// <param name="detail">What is wrong, as a whole sentence.</param>
    /// <returns>The exception.</returns>
    public static InvalidFilterException Because(string parameter, string detail) => new(parameter, detail);

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors =>
    [
        new()
        {
            Status = "422",
            Code = ErrorCodes.InvalidValue,
            Title = ErrorTitles.InvalidValue,
            Detail = _detail,
            Source = new ErrorSource { Parameter = _parameter },
        },
    ];
}
