using Bitwarden.Server.Sdk.Common.Errors;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The request was well formed but cannot be acted on.
/// </summary>
/// <remarks>
/// Answered with <c>422</c>, an error the client can fix. For a rule that can't be checked from the
/// request's shape alone, such as a name already taken, a reference that does not resolve, or a
/// state transition that is not allowed.
/// </remarks>
/// <param name="detail">What is wrong, in the caller's terms.</param>
/// <param name="pointer">A JSON Pointer to the offending field, when one field is to blame.</param>
/// <param name="code">A stable, machine-readable code. Defaults to <see cref="ErrorCodes.InvalidValue"/>.</param>
public sealed class InvalidRequestException(string detail, string? pointer = null, string? code = null)
    : Exception(detail)
{
    /// <summary>
    /// A JSON Pointer to the offending field, when one field is to blame.
    /// </summary>
    public string? Pointer { get; } = pointer;

    /// <summary>
    /// The stable code a caller branches on.
    /// </summary>
    public string Code { get; } = code ?? ErrorCodes.InvalidValue;
}
