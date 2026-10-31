using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// An attribute that decides, before a request is bound, whether the caller may invoke the
/// operation it is on: what an MVC authorization filter (<c>IAsyncAuthorizationFilter</c>) is to
/// an action.
/// </summary>
/// <remarks>
/// <para>
/// For a check the settled <see cref="AuthorizationAttribute"/> cannot express, such as a
/// permission that a provider's staff hold in a client organization. Put on a handler, or on its
/// class to apply to every handler in it.
/// </para>
/// <para>
/// It runs where an MVC controller's authorization filters run: after the authorization middleware,
/// before anything about the request is bound or validated, so a caller who may not invoke the
/// operation learns nothing about what a valid request would be. Refuse by throwing
/// (<c>ForbiddenException</c>); the platform answers in the standard shape.
/// </para>
/// </remarks>
public interface IAuthorizesOperation
{
    /// <summary>
    /// Refuses the request by throwing, or returns to let it through.
    /// </summary>
    /// <param name="context">The request.</param>
    ValueTask AuthorizeAsync(HttpContext context);
}
