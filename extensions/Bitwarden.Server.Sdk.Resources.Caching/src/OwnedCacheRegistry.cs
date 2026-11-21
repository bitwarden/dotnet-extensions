using Bitwarden.Server.Sdk.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Every resource the service caches, and what it needs to fill and invalidate them.
/// </summary>
/// <param name="queries">The cacheable queries, and the resource each declares.</param>
/// <param name="identity">The service's name, which is the cache's prefix.</param>
/// <param name="configuration">Switches a resource off without a release.</param>
internal sealed class OwnedCacheRegistry(
    CacheableQueries queries,
    IServiceIdentity identity,
    IConfiguration configuration)
{
    private readonly Dictionary<Type, IReadOnlyList<ICacheableResource>> _byEntity = queries.Resources
        .GroupBy(resource => resource.Entity)
        .ToDictionary(table => table.Key, table => (IReadOnlyList<ICacheableResource>)[.. table]);
    private readonly Lock _including = new();
    private IReadOnlyDictionary<Type, IReadOnlyList<Included>>? _included;

    /// <summary>
    /// A navigation a cached resource's read includes: a change to one of its rows moves the parent's version.
    /// </summary>
    /// <param name="Parent">The resource whose entry the row is part of.</param>
    /// <param name="Navigation">The parent's navigation to the row.</param>
    public sealed record Included(ICacheableResource Parent, INavigation Navigation);

    /// <summary>
    /// The resources whose entries rows of <paramref name="entity"/> are included in, through which navigation.
    /// </summary>
    /// <param name="context">A context of the service's database, for its model: read once, the first time.</param>
    /// <param name="entity">The row's type.</param>
    /// <exception cref="InvalidOperationException">A declared read includes what the cache can't invalidate.</exception>
    public IReadOnlyList<Included> IncludedIn(DbContext context, Type entity) =>
        Including(context).GetValueOrDefault(entity) ?? [];

    private IReadOnlyDictionary<Type, IReadOnlyList<Included>> Including(DbContext context)
    {
        if (Volatile.Read(ref _included) is { } known)
        {
            return known;
        }

        lock (_including)
        {
            _included ??= _byEntity.Values.SelectMany(table => table)
                .SelectMany(resource => resource.Included(context).Select(navigation => new Included(resource, navigation)))
                .GroupBy(included => included.Navigation.TargetEntityType.ClrType)
                .ToDictionary(table => table.Key, table => (IReadOnlyList<Included>)[.. table]);
            return _included;
        }
    }

    /// <summary>
    /// Whether rows of <paramref name="entity"/> are what a cached entry is built from: a resource's own, or ones its read
    /// includes. A write in bulk to such a table would never be invalidated.
    /// </summary>
    public bool BuiltFrom(DbContext context, Type entity) => _byEntity.ContainsKey(entity) || IncludedIn(context, entity).Count > 0;

    /// <summary>
    /// Every table something the cache holds is built from, with what a change to it does when the cache can't be told
    /// before it commits: <see cref="CacheWrites.Refuse"/> where any declaration reading it says so.
    /// </summary>
    /// <param name="context">A context of the service's database, for the tables the resources' reads include.</param>
    public IReadOnlyDictionary<Type, CacheWrites> Tables(DbContext context)
    {
        var tables = new Dictionary<Type, CacheWrites>();

        void Read(Type table, CacheWrites writes) =>
            tables[table] = tables.TryGetValue(table, out var already) && already == CacheWrites.Refuse ? already : writes;

        foreach (var resource in _byEntity.Values.SelectMany(table => table))
        {
            Read(resource.Entity, resource.WhenUnreachable);
        }

        foreach (var (table, included) in Including(context))
        {
            foreach (var each in included)
            {
                Read(table, each.Parent.WhenUnreachable);
            }
        }

        return tables;
    }

    /// <summary>
    /// How long a tombstone lives: as long as the longest-lived entry, ten minutes when there is none.
    /// </summary>
    public TimeSpan TombstoneLifetime =>
        _byEntity.Values.SelectMany(table => table).Select(resource => resource.Lifetime)
            .DefaultIfEmpty(TimeSpan.FromMinutes(10))
            .Max();

    /// <summary>
    /// The prefix every key the service owns starts with.
    /// </summary>
    public string Service => identity.Name;

    public IReadOnlyCollection<ICacheableResource> Resources => [.. _byEntity.Values.SelectMany(table => table)];

    /// <summary>
    /// The cacheable queries a table backs, each under its own key; none for a table nothing caches.
    /// </summary>
    public IReadOnlyList<ICacheableResource> For(Type entity) => _byEntity.GetValueOrDefault(entity) ?? [];

    /// <summary>
    /// Whether the cache may be filled with this resource. Off by configuration, for an incident:
    /// <c>Caching:Owned:Resources:{service}:{type}:Enabled=false</c>. Invalidation carries on, so
    /// switching it back on serves nothing stale.
    /// </summary>
    public bool Fills(ICacheableResource resource) =>
        configuration.GetValue($"{OwnedCacheEntry.ResourcesSection}:{Service}:{resource.ResourceType}:Enabled", true);
}
