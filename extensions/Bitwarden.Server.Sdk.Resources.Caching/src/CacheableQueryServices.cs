using Microsoft.EntityFrameworkCore;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// What a <see cref="CacheableQuery{TEntity,TResource}"/> reads with: the service's database, its cache, and the
/// request it serves. Given by <c>AddOwnedCache</c>, per request.
/// </summary>
public sealed class CacheableQueryServices
{
    internal CacheableQueryServices(IServiceProvider services, DbContext database)
    {
        Services = services;
        Database = database;
    }

    /// <summary>
    /// The request's services.
    /// </summary>
    internal IServiceProvider Services { get; }

    /// <summary>
    /// The service's database, so row-level security applies.
    /// </summary>
    internal DbContext Database { get; }
}
