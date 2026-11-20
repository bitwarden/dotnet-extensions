using System.Net;
using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// A call to a service failed in a way with no more specific exception.
/// </summary>
/// <remarks>
/// The typed exceptions (<c>NotFoundException</c>, <c>ConflictException</c> and the rest) are
/// what a caller is expected to catch, and are translated from the error document. This is what
/// remains: a status no code maps to, a response that is not an error document at all, or a
/// credential that could not be obtained.
/// </remarks>
public sealed class ServiceCallException : Exception
{
    /// <param name="status">The status the service answered with.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="response">The error document, when the service returned one.</param>
    public ServiceCallException(HttpStatusCode status, string message, ErrorResponse? response = null)
        : base(message)
    {
        Status = status;
        Response = response;
    }

    /// <param name="status">The status this failure is reported with.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="response">The error document, when the service returned one.</param>
    /// <param name="innerException">
    /// What it came from: for a credential Identity would not issue, an <see cref="HttpRequestException"/> carrying
    /// Identity's own status, while <see cref="Status"/> is <c>500</c>.
    /// </param>
    public ServiceCallException(HttpStatusCode status, string message, ErrorResponse? response, Exception? innerException)
        : base(message, innerException)
    {
        Status = status;
        Response = response;
    }

    /// <summary>
    /// The status the service answered with.
    /// </summary>
    /// <remarks>
    /// For a credential that could not be obtained, <c>500</c>: this service's own failure, whatever Identity
    /// answered, so no caller reads it as the callee's answer. Identity's status is the inner
    /// <see cref="HttpRequestException.StatusCode"/>.
    /// </remarks>
    public HttpStatusCode Status { get; }

    /// <summary>
    /// The error document, when the service returned one.
    /// </summary>
    public ErrorResponse? Response { get; }
}
