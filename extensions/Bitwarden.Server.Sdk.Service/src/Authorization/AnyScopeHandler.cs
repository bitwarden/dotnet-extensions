using Microsoft.AspNetCore.Authorization;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Admits a caller holding any one of the operation's scopes.
/// </summary>
/// <param name="caller">What the request names, for a forbidden provider.</param>
internal sealed class AnyScopeHandler(Bitwarden.Server.Sdk.Common.IContext caller) : AuthorizationHandler<AnyScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyScopeRequirement requirement)
    {
        // Left unsucceeded, as the platform's own handler leaves it, so an unauthenticated caller
        // is challenged with a 401 rather than forbidden.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (requirement.ForbidsProvider && caller.ProviderId is not null)
        {
            context.Fail(new AuthorizationFailureReason(
                this, $"The '{Bitwarden.Server.Sdk.Common.BitwardenHeaders.Provider}' header is not accepted by this operation."));
        }
        else if (requirement.Scopes.Any(scope => CallerScopes.Holds(context.User, scope)))
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail(new AuthorizationFailureReason(
                this,
                $"One of the {string.Join(", ", requirement.Scopes.Select(scope => $"'{scope}'"))} scopes is required."));
        }

        return Task.CompletedTask;
    }
}
