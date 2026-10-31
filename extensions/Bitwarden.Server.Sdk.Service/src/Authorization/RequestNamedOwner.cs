using Bitwarden.Server.Sdk.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bitwarden.Server.Sdk.Service;

/// <summary>
/// Answers <see cref="IRequestNamedOwner"/> for a product service: the organization or provider the
/// routed operation names, when it is declared to name one and the session names none.
/// </summary>
/// <param name="accessor">The request being handled.</param>
/// <param name="context">What the session names.</param>
internal sealed class RequestNamedOwner(IHttpContextAccessor accessor, IContext context) : IRequestNamedOwner
{
    internal static readonly object OrganizationKey = new();

    internal static readonly object ProviderKey = new();

    public Guid? OrganizationId =>
        context.OrganizationId is null
        && accessor.HttpContext is { } http
        && http.GetEndpoint()?.Metadata.GetMetadata<NamesOrganizationAttribute>() is not null
            ? Named(http, NamesOrganizationAttribute.RouteValue, OrganizationKey)
            : null;

    public Guid? ProviderId =>
        context.ProviderId is null
        && accessor.HttpContext is { } http
        && http.GetEndpoint()?.Metadata.GetMetadata<NamesProviderAttribute>() is not null
            ? Named(http, NamesProviderAttribute.RouteValue, ProviderKey)
            : null;

    private static Guid? Named(HttpContext http, string routeValue, object key) =>
        Guid.TryParse(http.GetRouteValue(routeValue)?.ToString(), out var fromPath) && fromPath != Guid.Empty
            ? fromPath
            : http.Items.TryGetValue(key, out var fromBody) && fromBody is Guid named && named != Guid.Empty
                ? named
                : null;
}
