using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A read-many other services may answer from this service's cache: every row of the party a request's context names
/// that one fixed condition selects, from one table, in one fixed order, kept as one entry.
/// </summary>
/// <typeparam name="TEntity">The one table it is read from.</typeparam>
/// <typeparam name="TResource">The resource each row is: its own, holding only what the row holds.</typeparam>
/// <remarks>
/// <para>
/// A list is cached only in this shape: at a path of its own, with no filter or sort, keyed by the caller alone, from
/// one table, nothing worked out per caller, at most a declared number of rows. Whatever could make the entry stale is a
/// change to a row of that table, which the change tracker sees: every insert, update and delete marks dead the entry of
/// the party the row named before and the one it names after, just before it commits.
/// </para>
/// <para>
/// The read is the platform's: the service's own entry, else the rows, narrowed to the party in context and the
/// declared condition, in the declared order, mapped and filled when there are no more than the declared most. A page
/// is a slice of them. Registered by type, as a cacheable query is; the operation serving it takes it, and its
/// fieldset, page number, page size and a cancellation token, and nothing else, so its answer is
/// <see cref="ReadAsync"/>'s.
/// </para>
/// </remarks>
public abstract class CacheableReadMany<TEntity, TResource> : ICacheableQuery
    where TEntity : class
    where TResource : Resource
{
    private readonly CacheableQueryServices _services;

    /// <summary>
    /// Gives the read what it reads with.
    /// </summary>
    /// <param name="services">The service's database, its cache and the request.</param>
    protected CacheableReadMany(CacheableQueryServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>
    /// Declares the read: whose rows, which, in what order, how each is shaped, the scope it is read with and how many
    /// at most an entry holds.
    /// </summary>
    /// <param name="list">The declaration.</param>
    /// <remarks>
    /// Runs once for the type, on an instance whose constructor hasn't run.
    /// </remarks>
    protected abstract void Configure(CacheableReadManyBuilder<TEntity, TResource> list);

    /// <summary>
    /// A page of the rows of the party the context names: from the service's own entry where a client would answer from
    /// it, otherwise from the database, filling the entry when it holds no more than the declared most.
    /// </summary>
    /// <param name="pageNumber">The page, from one.</param>
    /// <param name="pageSize">How many the page holds.</param>
    /// <param name="fields">The caller's sparse fieldset.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page's document.</returns>
    /// <exception cref="InvalidOperationException">The context names no party: its operation requires one.</exception>
    public async Task<PagedResourceListDocument<TResource>> ReadAsync(
        PageNumber pageNumber,
        PageSize pageSize,
        Fields<TResource> fields = default,
        CancellationToken cancellationToken = default)
    {
        var page = new Page(pageNumber, pageSize);
        var resource = Declared();
        var party = resource.ReadMany!.Party;

        var key = Named(_services.Services.GetRequiredService<IContext>(), party)
            ?? throw new InvalidOperationException(
                $"{GetType().Name} reads the {CacheFill.Axis(party)} in context's rows, and the context names none.");

        var paged = await AllAsync(resource, key, cancellationToken) is { } all
            ? new Paged<TResource>([.. all.Skip(page.Skip).Take(page.Size)], all.Count)
            : await PageAsync(resource, key, page, cancellationToken);

        return StandardResponses.ReadManyResponse(paged, page, fields);
    }

    ICacheableResource ICacheableQuery.Declare()
    {
        var builder = new CacheableReadManyBuilder<TEntity, TResource>();
        Configure(builder);
        return new CacheableReadManyResource<TEntity, TResource>(GetType(), builder);
    }

    /// <summary>
    /// Every row of the party, from the entry or the database, or null when the party has more than the declared most.
    /// </summary>
    private async Task<IReadOnlyList<TResource>?> AllAsync(
        CacheableReadManyResource<TEntity, TResource> resource,
        Guid key,
        CancellationToken cancellationToken)
    {
        var services = _services.Services;
        var store = services.GetRequiredService<IOwnedCacheStore>();
        var registry = services.GetRequiredService<OwnedCacheRegistry>();
        var entryKey = resource.EntryKey(registry.Service, key);
        var json = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var party = resource.ReadMany!.Party;
        var most = resource.ReadMany.AtMost;

        // Not inside a transaction: what it saved is invalidated only as it commits, so the entry may be older than the
        // transaction's own change.
        var fills = _services.Database.Database.CurrentTransaction is null && store.Enabled && registry.Fills(resource);

        if (fills && Own(await store.ReadAsync(entryKey), key, party, json) is { } own)
        {
            return own;
        }

        // Just before the query: a fill refuses what it read too long ago.
        var readStarted = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetTimestamp();

        // One more than the most an entry holds tells a party with more apart, without reading all of theirs.
        var rows = await Rows(resource, key).Take(most + 1).ToListAsync(cancellationToken);

        if (rows.Count > most)
        {
            return null;
        }

        IReadOnlyList<TResource> listed = [.. rows.Select(resource.Map)];

        if (fills && !CacheFill.TooOld(services, readStarted, entryKey))
        {
            var held = resource.Fields.ToHashSet(StringComparer.Ordinal);
            var data = new JsonArray([.. listed.Select(each =>
                JsonSerializer.SerializeToNode(new ResourceDocument<TResource> { Data = each, Fields = held }, json)!["data"]!.DeepClone())]);

            await store.FillAsync(
                entryKey,
                CacheableReadManyResource<TEntity, TResource>.FilledVersion,
                Owner(party, key),
                data.ToJsonString(),
                resource.Lifetime);
        }

        return listed;
    }

    /// <summary>
    /// A page of a party with more rows than an entry holds, from the database: never cached.
    /// </summary>
    private async Task<Paged<TResource>> PageAsync(
        CacheableReadManyResource<TEntity, TResource> resource,
        Guid key,
        Page page,
        CancellationToken cancellationToken)
    {
        var total = await Rows(resource, key).CountAsync(cancellationToken);
        var rows = await Rows(resource, key).Skip(page.Skip).Take(page.Size).ToListAsync(cancellationToken);

        return new Paged<TResource>([.. rows.Select(resource.Map)], total);
    }

    // Past row-level security, as a read across owners is, and narrowed instead to the party in context alone.
    private IQueryable<TEntity> Rows(CacheableReadManyResource<TEntity, TResource> resource, Guid key) =>
        resource.Under(_services.Database.Set<TEntity>().AsNoTracking().IgnoreQueryFilters(), key);

    private CacheableReadManyResource<TEntity, TResource> Declared() =>
        (CacheableReadManyResource<TEntity, TResource>)_services.Services.GetRequiredService<CacheableQueries>().For(GetType());

    private static Guid? Named(IContext context, Owner axis) => axis switch
    {
        Data.Owner.Organization => context.OrganizationId,
        Data.Owner.User => context.UserId,
        _ => context.ProviderId,
    };

    private static string Owner(Owner party, Guid key) =>
        new JsonObject { [CacheFill.Axis(party)] = key.ToString("D") }.ToJsonString();

    /// <summary>
    /// The service's own entry, when a client would answer from it: it exists, isn't a tombstone, is in this release's
    /// format, and is the party's; null otherwise.
    /// </summary>
    private static List<TResource>? Own(
        IReadOnlyDictionary<string, string>? entry,
        Guid key,
        Owner party,
        JsonSerializerOptions json)
    {
        if (entry is null
            || entry.ContainsKey(OwnedCacheEntry.Gone)
            || !entry.TryGetValue(OwnedCacheEntry.Format, out var format)
            || format != OwnedCacheEntry.CurrentFormat.ToString(CultureInfo.InvariantCulture)
            || !entry.TryGetValue(OwnedCacheEntry.Owner, out var stored)
            || CacheFill.OwnerOf(stored, party) != key
            || !entry.TryGetValue(OwnedCacheEntry.Data, out var data))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(data) is not JsonArray elements)
            {
                return null;
            }

            var read = new List<TResource>(elements.Count);

            foreach (var element in elements)
            {
                if (JsonSerializer.Deserialize<ResourceDocument<TResource>>(new JsonObject { ["data"] = element?.DeepClone() }, json)
                    is not { Data: { } resource })
                {
                    return null;
                }

                read.Add(resource);
            }

            return read;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
