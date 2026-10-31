using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A query parameter the service cannot act on.
/// </summary>
public sealed class InvalidQueryException : Exception, IProvideErrors
{
    private const string WholeNumber = "must be a whole number of 1 or more";

    private readonly string _parameter;
    private readonly string _value;
    private readonly string _expectation;

    /// <summary>
    /// Describes a parameter that is not a whole number of 1 or more.
    /// </summary>
    /// <param name="parameter">The parameter's name, as the caller sends it.</param>
    /// <param name="value">What the caller sent.</param>
    public InvalidQueryException(string parameter, string value)
        : this(parameter, value, WholeNumber)
    {
    }

    /// <summary>
    /// Describes a parameter that is not what it must be.
    /// </summary>
    /// <param name="parameter">The parameter's name, as the caller sends it.</param>
    /// <param name="value">What the caller sent.</param>
    /// <param name="expectation">What it must be, phrased to follow the parameter's name.</param>
    internal InvalidQueryException(string parameter, string value, string expectation)
        : base($"The '{parameter}' parameter {expectation}.")
    {
        _parameter = parameter;
        _value = value;
        _expectation = expectation;
    }

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors =>
    [
        new()
        {
            Status = "422",
            Code = ErrorCodes.InvalidValue,
            Title = ErrorTitles.InvalidValue,
            Detail = $"The '{_parameter}' parameter {_expectation}, not '{_value}'.",
            Source = new ErrorSource { Parameter = _parameter },
        },
    ];
}
