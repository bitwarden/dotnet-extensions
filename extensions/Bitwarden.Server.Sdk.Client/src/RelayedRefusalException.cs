using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// A service refused a request for a reason its caller's own caller has to hear as it was given:
/// the same status, the same errors.
/// </summary>
/// <remarks>
/// <para>
/// It provides its errors and status to a service host's exception handler, so a service that lets it
/// propagate answers exactly what it was answered.
/// </para>
/// </remarks>
/// <param name="statusCode">The status the service answered with.</param>
/// <param name="errors">The errors, in the order the service gave them.</param>
public sealed class RelayedRefusalException(int statusCode, IReadOnlyList<Error> errors)
    : Exception(errors is { Count: > 0 } ? errors[0].Detail ?? "The request was refused." : "The request was refused."),
        IProvideErrors
{
    /// <inheritdoc />
    public int StatusCode { get; } = statusCode;

    /// <inheritdoc />
    public IReadOnlyList<Error> Errors { get; } = errors;
}
