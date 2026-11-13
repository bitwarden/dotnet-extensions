using Bitwarden.Server.Sdk.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bitwarden.Server.Sdk.Service.Tests.TestHost;

/// <summary>
/// Endpoints whose shape somebody else fixed: the platform refuses nothing on their behalf.
/// </summary>
[AcceptsInvalidRequests]
[ParityFault]
public static class ParityRaw
{
    /// <summary>
    /// Maps them.
    /// </summary>
    /// <param name="endpoints">The service's endpoints.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var raw = endpoints.MapGroup("api/v1/parity-raw").WithStandards();
        raw.MapPost("echo", Echo);
        raw.MapPost("form", Form);
        raw.MapPost("nothing", Nothing);
        raw.MapGet("fault", Fault);
        raw.MapPost("gated", Gated);
        raw.MapPost("unread", Unread);
    }

    /// <summary>
    /// Echo a subject
    /// </summary>
    /// <param name="request">What to echo, if it could be read.</param>
    /// <returns>The subject</returns>
    public static IResult Echo([FromBody] ParityInput? request) =>
        request is null ? Results.BadRequest(new ParityAddress { City = "nothing read" }) : Results.Ok(request.Subject ?? "none");

    /// <summary>
    /// Read a form by its fields' names
    /// </summary>
    /// <param name="form">The form.</param>
    /// <returns>The grant</returns>
    public static IResult Form([FromForm] ParityForm form) => Results.Ok(form.GrantType ?? "none");

    /// <summary>
    /// Do nothing
    /// </summary>
    public static void Nothing()
    {
    }

    /// <summary>
    /// Fail in a shape of its own
    /// </summary>
    /// <returns>Never</returns>
    public static string Fault() => throw new InvalidTimeZoneException("faulted");

    /// <summary>
    /// Answer past the gate
    /// </summary>
    /// <param name="request">What to echo.</param>
    /// <returns>The subject</returns>
    [ParityGate]
    public static IResult Gated([FromBody] ParityInput? request) => Results.Ok(request?.Subject ?? "open");

    /// <summary>
    /// Say how much of a body the platform couldn't read was sent
    /// </summary>
    /// <param name="request">What was read, if anything.</param>
    /// <param name="http">The request.</param>
    /// <returns>The bytes kept, as text</returns>
    public static IResult Unread([FromBody] ParityInput? request, HttpContext http) =>
        Results.Ok(http.Features.Get<UnreadableRequestBody>() is { } unread
            ? System.Text.Encoding.UTF8.GetString(unread.Body.Span)
            : request is null ? "nothing kept" : "read");
}
