using Bitwarden.Server.Sdk.Common;
using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Service.Querying;

/// <summary>
/// A sparse fieldset naming something that is not one of the resource's attributes.
/// </summary>
/// <param name="parameter">The parameter's name, as the caller sends it: <c>fields[type]</c>.</param>
/// <param name="attribute">The name that matched no attribute.</param>
public sealed class InvalidFieldsException(string parameter, string attribute)
    : Exception($"The '{parameter}' parameter names '{attribute}', which is not one of the resource's attributes."),
        IProvideErrors
{
    /// <inheritdoc />
    public IReadOnlyList<Error> Errors =>
    [
        new()
        {
            Status = "422",
            Code = ErrorCodes.InvalidValue,
            Title = ErrorTitles.InvalidValue,
            Detail = $"The '{parameter}' parameter names '{attribute}', which is not one of the resource's attributes.",
            Source = new ErrorSource { Parameter = parameter },
        },
    ];
}
