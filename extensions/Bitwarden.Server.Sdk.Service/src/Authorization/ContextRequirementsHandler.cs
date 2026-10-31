using System.Security.Claims;
using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Decides whether a caller satisfies an operation's <see cref="AuthorizationAttribute"/>.
/// </summary>
/// <param name="context">What the current request carries about its caller.</param>
/// <param name="caller">What the caller's session says they may do.</param>
/// <param name="identity">Whether this is a product service, where a person's session ends.</param>
internal sealed class ContextRequirementsHandler(IContext context, ICallerRole caller, IServiceIdentity identity)
    : AuthorizationHandler<ContextRequirements>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext authorization,
        ContextRequirements requirement)
    {
        var options = requirement.Options;

        if (options.Anonymous)
        {
            authorization.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (authorization.User.Identity?.IsAuthenticated != true)
        {
            // Left unsucceeded rather than explicitly failed, so the framework challenges with a
            // 401 instead of forbidding with a 403: the caller may simply not have presented a
            // token yet.
            return Task.CompletedTask;
        }

        // An organization credential to an operation that accepts one (product services only):
        // the scope it must hold is the one the operation declares for it, and it names no person.
        var credential = identity.IsProductService && authorization.Resource is HttpContext http
            && OrganizationCredential.Accepted(http) is not null
                ? OrganizationCredential.DeclaredBy(http)
                : null;

        var required = credential?.Scope ?? options.Scope;

        if (required is { Length: > 0 } scope && !Scopes(authorization.User).Contains(scope, StringComparer.Ordinal))
        {
            authorization.Fail(new AuthorizationFailureReason(this, $"The '{scope}' scope is required."));
            return Task.CompletedTask;
        }

        if (!Satisfies(options.Organization, context.OrganizationId is not null))
        {
            authorization.Fail(new AuthorizationFailureReason(
                this, Describe(options.Organization, BitwardenHeaders.Organization, "an organization")));
            return Task.CompletedTask;
        }

        // Not asked of an organization credential: no person is acting, and the operation's
        // declaration is that it accepts a request with nobody behind it.
        if (credential is null && !Satisfies(options.User, context.UserId is not null))
        {
            authorization.Fail(new AuthorizationFailureReason(
                this, Describe(options.User, BitwardenHeaders.User, "a person")));
            return Task.CompletedTask;
        }

        if (!Satisfies(options.Provider, context.ProviderId is not null))
        {
            authorization.Fail(new AuthorizationFailureReason(
                this, Describe(options.Provider, BitwardenHeaders.Provider, "a provider")));
            return Task.CompletedTask;
        }

        // Below the product layer both of these are left at their defaults and neither runs, so
        // a domain service pays nothing for them and cannot accidentally depend on a session it
        // will never see.
        if (options.OrganizationRole != OrganizationRole.None && caller.Role < options.OrganizationRole)
        {
            authorization.Fail(new AuthorizationFailureReason(
                this, $"The caller must be at least a {options.OrganizationRole} of the organization."));
            return Task.CompletedTask;
        }

        if (options.ProviderRole != ProviderRole.None && caller.ProviderRole < options.ProviderRole)
        {
            authorization.Fail(new AuthorizationFailureReason(
                this, $"The caller must be at least a {options.ProviderRole} of the provider."));
            return Task.CompletedTask;
        }

        if (!caller.Holds(options.Permission))
        {
            authorization.Fail(new AuthorizationFailureReason(
                this, $"The caller must hold the {options.Permission} permission."));
            return Task.CompletedTask;
        }

        authorization.Succeed(requirement);
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Read from the token rather than from <see cref="IContext"/>. What a caller may do is an
    /// authentication artifact and is proven; what a call is about is asserted in headers. The
    /// two travel differently and are kept apart.
    /// </remarks>
    private static IEnumerable<string> Scopes(ClaimsPrincipal caller) =>
        caller.FindAll("scope").Select(claim => claim.Value);

    private static bool Satisfies(ContextRequirement requirement, bool present) => requirement switch
    {
        ContextRequirement.Required => present,
        ContextRequirement.Forbidden => !present,
        _ => true,
    };

    /// <summary>
    /// Why a requirement refused the request: in a product service, what the session names (the
    /// only place its context comes from); below it, the header the calling service
    /// sent or didn't.
    /// </summary>
    private string Describe(ContextRequirement requirement, string header, string what) =>
        identity.IsProductService
            ? requirement == ContextRequirement.Required
                ? $"The session must name {what}."
                : $"This operation does not accept a session that names {what}."
            : requirement == ContextRequirement.Required
                ? $"The '{header}' header is required."
                : $"The '{header}' header is not accepted by this operation.";
}
