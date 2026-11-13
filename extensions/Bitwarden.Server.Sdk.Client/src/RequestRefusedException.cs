using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// A service refused a request for more than one reason, and said what each was.
/// </summary>
/// <remarks>
/// <para>
/// The many-error form of <see cref="InvalidRequestException"/>, which carries one. A service
/// that validates exhaustively, as the standards ask, answers <c>422</c> with every problem it
/// found, and an all-or-nothing bulk operation answers with every item that failed; reducing
/// that to the first would tell a caller to fix one thing and try again, and again.
/// </para>
/// <para>
/// It provides its errors to a service host's exception handler, so a service that lets it
/// propagate answers its own caller <c>422</c> with the same errors: the same codes and the
/// same pointers. Those pointers name places in the request this service sent; a service that
/// reshapes a request before passing it on should translate them, and one that passes the
/// request on as it came need not.
/// </para>
/// </remarks>
/// <param name="errors">The errors, in the order the service gave them.</param>
public sealed class RequestRefusedException(IReadOnlyList<Error> errors)
    : Exception(errors is { Count: > 0 } ? errors[0].Detail ?? "The request was refused." : "The request was refused."),
        IProvideErrors
{
    /// <inheritdoc />
    public IReadOnlyList<Error> Errors { get; } = errors;
}
