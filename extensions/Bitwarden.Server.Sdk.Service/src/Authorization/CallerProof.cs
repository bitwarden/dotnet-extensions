using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// What proves whom the request being handled is for (<see cref="ICallerProof"/>), for this service's
/// own calls to exchange their context token from.
/// </summary>
/// <remarks>
/// At a product service, the token it validated, when it names a party (a person, an organization
/// or a machine account; never a Send's); at a domain service, the context token the call carried,
/// once verified. Holds nothing: it reads the request, as <see cref="Context"/> does.
/// </remarks>
/// <param name="accessor">Supplies the request being handled.</param>
/// <param name="context">The request's context, for the kind of credential a product service validated.</param>
/// <param name="isProductService">Whether the proof is the session rather than a context token.</param>
internal sealed class CallerProof(IHttpContextAccessor accessor, IContext context, bool isProductService) : ICallerProof
{
    private const string Bearer = "Bearer ";

    /// <inheritdoc />
    public string? SubjectToken => accessor.HttpContext is not { } http
        ? null
        : isProductService
            ? http.User.Identity?.IsAuthenticated == true
              && context.Credential is not null
              && http.Request.Headers.Authorization.FirstOrDefault() is { } authorization
              && authorization.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase)
                ? authorization[Bearer.Length..].Trim()
                : null
            : CallerContextHeaders.Verified(http)?.Token;
}
