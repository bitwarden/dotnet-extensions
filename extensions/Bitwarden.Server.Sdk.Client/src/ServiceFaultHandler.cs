using System.Net;
using System.Net.Http.Json;
using Bitwarden.Server.Sdk.Errors;

namespace Bitwarden.Server.Sdk.Client;

/// <summary>
/// Turns an unsuccessful response into the exception it means.
/// </summary>
/// <remarks>
/// <para>
/// A caller should write <c>catch (NotFoundException)</c>, not read a status code and parse an
/// error document. The service already said what went wrong in a machine-readable code; this
/// translates that code into the same exception type the service itself would have thrown, so
/// both sides of a call share one vocabulary.
/// </para>
/// <para>
/// A handler rather than something the client does, so it holds for every client: generated,
/// hand-written, or written before this package existed.
/// </para>
/// <para>
/// A service that cannot be reached, does not answer in time, or answers <c>502</c>, <c>503</c> or
/// <c>504</c> becomes a <see cref="ServiceUnavailableException"/> naming it, which the calling
/// service answers with <c>503</c>. One that answers <c>500</c> is up and has a bug, and stays a
/// failure of its own.
/// </para>
/// <para>
/// The first handler registered, so the outermost: the time allowed covers obtaining the call's tokens from Identity
/// as well as the call. Identity failing then is said as Identity (<see cref="ServiceTokenProvider.Issuer"/>), never as
/// the callee, which was never asked.
/// </para>
/// <para>
/// The time allowed is kept here rather than on the <see cref="HttpClient"/>, because only here can
/// a dependency that did not answer be told apart from a caller that stopped waiting: the one is
/// this service's deadline passing, the other is the caller's token. Only the first is the
/// dependency's fault.
/// </para>
/// </remarks>
/// <param name="service">The service this client calls, for what the caller is told.</param>
/// <param name="timeout">How long a call may take.</param>
internal sealed class ServiceFaultHandler(string service, TimeSpan timeout) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        request.Options.Set(Deadline, new CallDeadline(cancellationToken, deadline.Token));

        try
        {
            response = await base.SendAsync(request, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Still waiting on Identity for the call's token: Identity is what did not answer, and the callee
            // was never asked.
            throw request.Options.TryGetValue(Stage, out var obtaining) && obtaining is not null
                ? new ServiceUnavailableException(
                    ServiceTokenProvider.Issuer,
                    string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"did not issue {obtaining} for calls to {service} within {timeout.TotalSeconds:0.###} seconds"))
                : ServiceUnavailableException.TimedOut(service, timeout);
        }
        catch (HttpRequestException exception)
        {
            throw ServiceUnavailableException.Unreachable(service, exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // A refusal's error document is read within the call's deadline too: a callee that sends its status and then
        // stalls is not answering in time. The response is let go of whatever is thrown.
        using (response)
        {
            try
            {
                throw await RefusalAsync(request, response, deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw ServiceUnavailableException.TimedOut(service, timeout);
            }
        }
    }

    /// <summary>
    /// The exception an unsuccessful response means.
    /// </summary>
    private async Task<Exception> RefusalAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // A person's keys rotating: relayed as it was answered, whatever the status.
        // Read once here, and reused below for a conflict that is something else.
        ErrorResponse? read = null;
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable)
        {
            read = await ReadAsync(response, cancellationToken);

            if (read is { Errors: [{ Code: ErrorCodes.KeyRotationInProgress }, ..] })
            {
                return new RelayedRefusalException((int)response.StatusCode, read.Errors);
            }
        }

        // Still running an earlier attempt of this write after the retries: the callee hasn't answered it yet, which
        // is it being unavailable for now, not the caller's conflict.
        if (Transient.StillRunning(request, response))
        {
            return new ServiceUnavailableException(service, "is still finishing an earlier attempt of this write");
        }

        if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            return ServiceUnavailableException.Answered(service, (int)response.StatusCode);
        }

        var document = read ?? await ReadAsync(response, cancellationToken);
        var error = document?.Errors.FirstOrDefault();

        return Translate(response.StatusCode, error, document);
    }

    /// <summary>
    /// The call's deadline and its caller's cancellation, for the handlers inside this one to tell apart.
    /// </summary>
    private static readonly HttpRequestOptionsKey<CallDeadline?> Deadline = new("Bitwarden.Client.Deadline");

    /// <summary>
    /// Whether the call's deadline has passed while its caller still waits: the callee not answering in time, as opposed
    /// to the caller giving up.
    /// </summary>
    /// <param name="request">The call.</param>
    internal static bool DeadlinePassed(HttpRequestMessage request) =>
        request.Options.TryGetValue(Deadline, out var deadline)
        && deadline is { } call
        && call.Deadline.IsCancellationRequested
        && !call.Caller.IsCancellationRequested;

    private sealed record CallDeadline(CancellationToken Caller, CancellationToken Deadline);

    /// <summary>
    /// What the call is waiting on Identity for, while it is; set by the handlers inside this one.
    /// </summary>
    private static readonly HttpRequestOptionsKey<string?> Stage = new("Bitwarden.Client.Obtaining");

    /// <summary>
    /// Marks <paramref name="request"/> as waiting on Identity for <paramref name="what"/>, so the deadline passing
    /// then is reported as Identity not answering rather than the callee. Cleared by <see cref="Obtained"/> once it
    /// has, and only then: a failure leaves the mark for this handler to read.
    /// </summary>
    /// <param name="request">The call.</param>
    /// <param name="what">What is being obtained, completing "did not issue …".</param>
    internal static void Obtaining(HttpRequestMessage request, string what) => request.Options.Set(Stage, what);

    /// <summary>
    /// Clears <see cref="Obtaining"/>: the call is no longer waiting on Identity.
    /// </summary>
    /// <param name="request">The call.</param>
    internal static void Obtained(HttpRequestMessage request) => request.Options.Set(Stage, null);

    private static async Task<ErrorResponse?> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync(
                ServiceClientJsonContext.Default.ErrorResponse, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            // A failure that is not one of ours: a proxy's HTML error page, say. The status
            // still tells the caller something, so it is reported rather than replaced.
            return null;
        }
    }

    /// <remarks>
    /// Each of these exceptions carries its own default wording, which is the right thing to use
    /// when a service gave no detail rather than the empty string.
    /// </remarks>
    private static Exception Detailed(Error error, Func<string, Exception> with, Func<Exception> without) =>
        string.IsNullOrWhiteSpace(error.Detail) ? without() : with(error.Detail);

    /// <remarks>
    /// Keyed on the code rather than the status, because the code is what the standards call
    /// contract. Status is the fallback for a failure that carried no error document at all.
    /// </remarks>
    private static Exception Translate(HttpStatusCode status, Error? error, ErrorResponse? document) =>
        error?.Code switch
        {
            ErrorCodes.NotFound => Detailed(error, detail => new NotFoundException(detail), () => new NotFoundException()),
            ErrorCodes.Conflict => Detailed(error, detail => new ConflictException(detail), () => new ConflictException()),
            ErrorCodes.Unauthenticated => new UnauthenticatedException(error.Detail),
            ErrorCodes.Forbidden => new ForbiddenException(error.Detail),
            ErrorCodes.NotImplemented => new NotImplementedException(error.Detail),
            // More than one thing wrong with the request: every one of them, not the first. A
            // caller serving somebody else passes them all on, as the standards ask a response to
            // report every failure, an all-or-nothing bulk refusal especially.
            ErrorCodes.FieldRequired or ErrorCodes.InvalidType or ErrorCodes.InvalidValue
                or ErrorCodes.InvalidShape or ErrorCodes.MalformedJson or ErrorCodes.AlreadyExists
                or ErrorCodes.InvalidResourceType
                when status == HttpStatusCode.UnprocessableEntity && document is { Errors.Count: > 1 } =>
                new RequestRefusedException(document.Errors),
            ErrorCodes.FieldRequired or ErrorCodes.InvalidType or ErrorCodes.InvalidValue
                or ErrorCodes.InvalidShape or ErrorCodes.MalformedJson or ErrorCodes.AlreadyExists
                or ErrorCodes.InvalidResourceType =>
                new InvalidRequestException(error.Detail ?? string.Empty, error.Source?.Pointer, error.Code),
            _ => new ServiceCallException(
                status,
                error?.Detail ?? $"The service answered {(int)status} {status}.",
                document),
        };
}
