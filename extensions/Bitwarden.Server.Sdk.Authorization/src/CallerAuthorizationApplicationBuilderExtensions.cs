using Bitwarden.Server.Sdk.Authorization.CallChains;
using Microsoft.AspNetCore.Builder;

namespace Bitwarden.Server.Sdk.Authorization;

/// <summary>
/// Adds context authorization's middleware to the pipeline, each at the point a service's pipeline needs it.
/// </summary>
public static class CallerAuthorizationApplicationBuilderExtensions
{
    /// <summary>
    /// Takes the address and scheme a request was forwarded from only from a trusted proxy (<c>ForwardedHeaders:*</c>),
    /// and never from what a client wrote.
    /// </summary>
    /// <remarks>
    /// Add it before anything reads the connection's address or scheme.
    /// </remarks>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseForwardedClientAddress(this IApplicationBuilder app) =>
        app.UseMiddleware<ForwardedClientAddress>();

    /// <summary>
    /// Refuses a request to a product service that carries context headers, which only another service sends.
    /// </summary>
    /// <remarks>
    /// Add it before anything reads the context and before authentication, so the answer is the same whatever the
    /// token.
    /// </remarks>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseContextRefusal(this IApplicationBuilder app) =>
        app.UseMiddleware<ContextRefusal>();

    /// <summary>
    /// At a domain service, believes another service's call only as its signed context token says: a missing or forged
    /// token is refused <c>401</c>, and a malformed or disagreeing context <c>400</c>.
    /// </summary>
    /// <remarks>
    /// Add it after authentication and before authorization.
    /// </remarks>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseContextHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<ContextHeaders>();

    /// <summary>
    /// At a product service, reads the person's current standing in the organization or provider their session names,
    /// once per request, and refuses a request from somebody who no longer holds it (<see cref="ICurrentStanding"/>):
    /// <c>404</c> when they are no longer a member, <c>403</c> when a client organization's session is no longer the
    /// provider's staff. <see cref="ICallerRole"/> answers from what it read.
    /// </summary>
    /// <remarks>
    /// Add it after authentication and before authorization. The service must register an
    /// <see cref="ICurrentStanding"/>; without one the pipeline fails to build, so the service fails at startup.
    /// </remarks>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseCurrentStanding(this IApplicationBuilder app) =>
        app.UseMiddleware<CurrentStandingCheck>();

    /// <summary>
    /// Keeps the chain of services a call passed through, believed from another service only, and notices a call back
    /// into this service while one of its own requests waits.
    /// </summary>
    /// <remarks>
    /// Add it after authorization, so an operation naming its own scheme has authenticated its caller.
    /// </remarks>
    /// <param name="app">The pipeline.</param>
    /// <returns>The pipeline.</returns>
    public static IApplicationBuilder UseCallChainCheck(this IApplicationBuilder app) =>
        app.UseMiddleware<CallChainCheck>();
}
