using Bitwarden.Server.Sdk.Common.JsonApi;

namespace Bitwarden.Server.Sdk.Common;

/// <summary>
/// The request was well formed but cannot be acted on.
/// </summary>
/// <remarks>
/// Answered with <c>422</c> — the standards' "an error the client can fix". This is for a rule the
/// platform cannot check from the request alone: a name already taken, a reference that does not
/// resolve, a state transition that is not allowed. Shape, type and required-ness are already
/// settled before a service sees the request.
/// </remarks>
/// <param name="detail">What is wrong, in the caller's terms.</param>
/// <param name="pointer">A JSON Pointer to the offending field, when one field is to blame.</param>
/// <param name="code">A stable, machine-readable code. Defaults to <see cref="ErrorCodes.InvalidValue"/>.</param>
public sealed class InvalidRequestException(string detail, string? pointer = null, string? code = null)
    : Exception(detail)
{
    /// <summary>A JSON Pointer to the offending field, when one field is to blame.</summary>
    public string? Pointer { get; } = pointer;

    /// <summary>The stable code a caller branches on.</summary>
    public string Code { get; } = code ?? ErrorCodes.InvalidValue;
}
