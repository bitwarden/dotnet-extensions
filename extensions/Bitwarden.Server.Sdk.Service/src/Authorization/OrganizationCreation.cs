using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Allows the organization a request just created to be named on its outgoing calls, on an
/// operation declared to create one.
/// </summary>
/// <param name="accessor">The request being handled.</param>
/// <param name="context">What the request names.</param>
internal sealed class OrganizationCreation(IHttpContextAccessor accessor, IContext context) : IOrganizationCreation
{
    private static readonly object Key = new();

    // Calls the request makes at once may each name an organization: only one may be first. Taken only on an
    // operation that creates an organization.
    private static readonly Lock Claiming = new();

    public bool Allows(Guid organization)
    {
        if (accessor.HttpContext is not { } http
            || http.GetEndpoint()?.Metadata.GetMetadata<CreatesOrganizationAttribute>() is null
            || context.OrganizationId is not null)
        {
            return false;
        }

        // The first organization named is the request's; any later call must name the same one.
        lock (Claiming)
        {
            if (http.Items.TryGetValue(Key, out var named))
            {
                return named is Guid first && first == organization;
            }

            http.Items[Key] = organization;
            return true;
        }
    }
}
