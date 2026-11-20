using Bitwarden.Server.Sdk.Authorization;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Resources.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Settles the cache contract over every endpoint, once they are built.
/// </summary>
/// <param name="queries">The cacheable queries.</param>
/// <param name="identity">Whether this is a domain service.</param>
/// <param name="providers">What packages and the service add as filters, which a cacheable read may not have.</param>
internal sealed class CacheContractCheck(
    CacheableQueries queries,
    IServiceIdentity identity,
    IEnumerable<IStandardEndpointFilterProvider> providers) : IStandardEndpointCheck
{
    public void Check(IReadOnlyList<Endpoint> endpoints, ICollection<string> problems)
    {
        var operations = new List<CacheOperation>();
        var seen = new HashSet<System.Reflection.MethodInfo>();

        foreach (var endpoint in endpoints)
        {
            var metadata = endpoint.Metadata;
            if (metadata.GetMetadata<StandardEndpointMetadata>() is null
                || metadata.GetMetadata<System.Reflection.MethodInfo>() is not { } method
                || !seen.Add(method))
            {
                continue;
            }

            operations.Add(new CacheOperation(
                $"{method.DeclaringType?.Name}.{method.Name}",
                method,
                metadata.OfType<AuthorizationAttribute>().LastOrDefault(attribute => attribute.Settled),
                Filters(method, metadata),
                [.. metadata]));
        }

        CacheContract.Settle(operations, queries, identity, problems.Add);
    }

    /// <summary>
    /// The filters around the handler besides the platform's own: added to the endpoint or its group, carried as
    /// attributes by the handler or its class, or added by a package or the service for it.
    /// </summary>
    private int Filters(System.Reflection.MethodInfo method, EndpointMetadataCollection metadata)
    {
        var scratch = new List<string>();

        return (metadata.GetMetadata<EndpointFilters>()?.Count ?? 0)
            + method.GetCustomAttributes(inherit: true).OfType<IEndpointFilter>().Count()
            + (method.DeclaringType?.GetCustomAttributes(inherit: true).OfType<IEndpointFilter>().Count() ?? 0)
            + providers.Count(provider => provider.For(method, scratch) is not null);
    }
}
