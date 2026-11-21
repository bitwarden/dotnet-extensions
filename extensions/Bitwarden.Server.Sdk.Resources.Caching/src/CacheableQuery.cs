using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Context;
using Bitwarden.Server.Sdk.Data;
using Bitwarden.Server.Sdk.Errors;
using Bitwarden.Server.Sdk.Resources.Querying;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// A read of one resource that other services may answer from this service's cache: the service's own read, and the
/// only way the resource is read by its identifier or key.
/// </summary>
/// <typeparam name="TEntity">The row it is read from.</typeparam>
/// <typeparam name="TResource">The resource.</typeparam>
/// <remarks>
/// <para>
/// A query declares its read in <see cref="Configure"/>, and the read itself is the platform's
/// (<see cref="ReadAsync"/>, <see cref="ReadByKeyAsync"/>), so the answer a client takes from the cache is the answer
/// the service gives:
/// </para>
/// <list type="number">
///   <item><description>
///     the service's own entry, when it holds every attribute asked for and the checks a client makes pass: it exists,
///     isn't a tombstone, is in a format this release understands, and its owner is the one the call names;
///   </description></item>
///   <item><description>
///     otherwise the row, found through the context so row-level security narrows it, with what
///     <see cref="CacheableQueryBuilder{TEntity,TResource}.Including"/> loads, and mapped;
///   </description></item>
///   <item><description>
///     the whole mapped resource, less what <see cref="CacheableQueryBuilder{TEntity,TResource}.PerCaller"/> names, is
///     written to the entry, unless the read began longer ago than the fill limit, ran inside a transaction, or a change
///     has marked its version dead; then the attributes worked out per caller are added (<see cref="CompleteAsync"/>)
///     when asked for.
///   </description></item>
/// </list>
/// <para>
/// Registered like any other query, by type (<c>services.TryAddScoped&lt;IGetGroupQuery, GetGroupQuery&gt;()</c>),
/// which turns caching on for its resource; the service also calls <c>AddOwnedCache</c>. A handler taking the query is
/// the cacheable operation, published in the contract as <c>x-bitwarden-cache</c>, and may take nothing else but the
/// identifier or key, the fieldset and a cancellation token.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class GetGroupQuery(CacheableQueryServices services)
///     : CacheableQuery&lt;GroupEntity, Group&gt;(services), IGetGroupQuery
/// {
///     protected override void Configure(CacheableQueryBuilder&lt;GroupEntity, Group&gt; query) => query
///         .Map(GroupMapping.ToResource)
///         .Lifetime(TimeSpan.FromMinutes(10));
/// }
/// </code>
/// </example>
public abstract class CacheableQuery<TEntity, TResource> : ICacheableQuery
    where TEntity : class
    where TResource : Resource
{
    private readonly CacheableQueryServices _services;

    /// <summary>
    /// Gives the query what it reads with.
    /// </summary>
    /// <param name="services">The service's database, its cache and the request.</param>
    protected CacheableQuery(CacheableQueryServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <summary>
    /// Declares the read: the map, what it includes, who owns the row, what is worked out per caller, and how long an
    /// entry lives.
    /// </summary>
    /// <param name="query">The declaration.</param>
    /// <remarks>
    /// Runs once for the type, on an instance whose constructor hasn't run, so it can't depend on what the query is
    /// given: a declaration is the same for every caller.
    /// </remarks>
    protected abstract void Configure(CacheableQueryBuilder<TEntity, TResource> query);

    /// <summary>
    /// Works out the attributes <see cref="CacheableQueryBuilder{TEntity,TResource}.PerCaller"/> names, for this caller.
    /// </summary>
    /// <param name="entity">The row, as read for this request.</param>
    /// <param name="resource">The map of the row.</param>
    /// <param name="fields">The caller's sparse fieldset: every attribute when they sent none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The resource with the per-caller attributes set.</returns>
    /// <remarks>
    /// Called only when the fieldset asks for at least one of them, after the row is read from the database, never from
    /// the cache. It sets those attributes and nothing else: it cannot refuse the read or hide the row, so the read's
    /// access rules stay the declaration's. Ask <see cref="Fields{T}.Includes"/> before working one out.
    /// </remarks>
    protected virtual ValueTask<TResource> CompleteAsync(
        TEntity entity,
        TResource resource,
        Fields<TResource> fields,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(resource);

    /// <summary>
    /// Reads the resource with this identifier.
    /// </summary>
    /// <param name="id">The identifier, as the caller sent it.</param>
    /// <param name="fields">The caller's sparse fieldset: every attribute when they sent none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The resource, shaped to the fieldset.</returns>
    /// <exception cref="NotFoundException">
    /// No such resource the caller may read, or an identifier that cannot be one.
    /// </exception>
    /// <exception cref="InvalidRequestException">A row that is its own owner, and the request names another.</exception>
    /// <exception cref="InvalidOperationException">The query reads by a unique key, not by identifier.</exception>
    public async Task<ResourceDocument<TResource>> ReadAsync(
        string id,
        Fields<TResource> fields = default,
        CancellationToken cancellationToken = default)
    {
        var resource = Declared();

        if (!resource.ById)
        {
            throw new InvalidOperationException($"{GetType().Name} reads {typeof(TResource).Name} by its {resource.KeyName}, not by identifier.");
        }

        if (!Guid.TryParse(id, out var key))
        {
            // An identifier that cannot be one is indistinguishable, to a caller, from one that
            // does not exist; saying which would leak whether the format is meaningful.
            throw new NotFoundException();
        }

        resource.Read.RequireOwnerNamed(key, _services.Services.GetRequiredService<IContext>());

        return await ReadCoreAsync(resource, key, null, fields, cancellationToken) ?? throw new NotFoundException();
    }

    /// <summary>
    /// Reads the resource with this value of the unique key it is declared
    /// <see cref="O:Bitwarden.Server.Sdk.Resources.Caching.CacheableQueryBuilder`2.KeyedBy"/>, or null when nothing is recorded under it.
    /// </summary>
    /// <param name="key">The key's value: an organization, for entitlements.</param>
    /// <param name="fields">The caller's sparse fieldset: every attribute when they sent none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The resource shaped to the fieldset, or null when nothing is recorded under the key.</returns>
    /// <exception cref="InvalidOperationException">The query reads by identifier, not by a key.</exception>
    public Task<ResourceDocument<TResource>?> ReadByKeyAsync(
        Guid key,
        Fields<TResource> fields = default,
        CancellationToken cancellationToken = default)
    {
        var resource = Declared();

        if (resource.ById)
        {
            throw new InvalidOperationException($"{GetType().Name} reads {typeof(TResource).Name} by identifier, not by a key.");
        }

        return ReadCoreAsync(resource, key, OwnerInContext(resource), fields, cancellationToken);
    }

    /// <summary>
    /// Reads the resource at <c>current</c>: under the key the context names, as declared
    /// <see cref="CacheableQueryBuilder{TEntity,TResource}.CurrentFrom"/>, within the owner the context names for a key
    /// within its owner; null when nothing is recorded under it.
    /// </summary>
    /// <param name="fields">The caller's sparse fieldset: every attribute when they sent none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The resource shaped to the fieldset, or null when nothing is recorded under the key.</returns>
    /// <exception cref="InvalidOperationException">
    /// The query declares no read of <c>current</c>, or the context names nobody to read: its operation must require
    /// them, which the platform checks as the host starts.
    /// </exception>
    public Task<ResourceDocument<TResource>?> ReadCurrentAsync(
        Fields<TResource> fields = default,
        CancellationToken cancellationToken = default)
    {
        var resource = Declared();

        if (resource.CurrentFrom is not { } axis)
        {
            throw new InvalidOperationException($"{GetType().Name} declares no read of current (CurrentFrom).");
        }

        var key = Named(_services.Services.GetRequiredService<IContext>(), axis)
            ?? throw new InvalidOperationException(
                $"{GetType().Name} reads current by the {CacheFill.Axis(axis)} in context, and the context names none.");

        return ReadCoreAsync(resource, key, OwnerInContext(resource), fields, cancellationToken);
    }

    /// <summary>
    /// Reads the resource under this key within the owner named, for the service's own work about a party its request
    /// doesn't name: a membership's provider staff, worked out from the provider's own rows in an organization's
    /// request. Narrowed by the owner named, as row-level security narrows a request naming it, and checked against it
    /// when answered from the entry; filled as any read is, so a client of the cache finds it.
    /// </summary>
    /// <param name="owner">The owner: the key itself for a key that is the owner, else the owner a key within its owner is within.</param>
    /// <param name="key">The key.</param>
    /// <param name="fields">The caller's sparse fieldset: every attribute when they sent none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The resource, or null when nothing is recorded under the key.</returns>
    /// <exception cref="InvalidOperationException">
    /// The query is keyed neither by its owner nor within it, so naming the owner wouldn't narrow the read.
    /// </exception>
    public Task<ResourceDocument<TResource>?> ReadOwnedAsync(
        Guid owner,
        Guid key,
        Fields<TResource> fields = default,
        CancellationToken cancellationToken = default)
    {
        var resource = Declared();

        if (!resource.WithinOwner && !(resource.KeyIsOwner && owner == key))
        {
            throw new InvalidOperationException(
                $"{GetType().Name} reads {typeof(TResource).Name} by a key that doesn't name its owner, so it can't be read about an owner named outright.");
        }

        return ReadCoreAsync(resource, key, resource.WithinOwner ? owner : null, fields, cancellationToken, owner);
    }

    private Guid? OwnerInContext(CacheableResource<TEntity, TResource> resource)
    {
        if (!resource.WithinOwner)
        {
            return null;
        }

        return Named(_services.Services.GetRequiredService<IContext>(), resource.Owner!.Value)
            ?? throw new InvalidOperationException(
                $"{GetType().Name} reads {typeof(TResource).Name} within its {CacheFill.Axis(resource.Owner!.Value)}, "
                + "and the context names none.");
    }

    private static Guid? Named(IContext context, Owner axis) => axis switch
    {
        Owner.Organization => context.OrganizationId,
        Owner.User => context.UserId,
        _ => context.ProviderId,
    };

    ICacheableResource ICacheableQuery.Declare()
    {
        var builder = new CacheableQueryBuilder<TEntity, TResource>();
        Configure(builder);
        return new CacheableResource<TEntity, TResource>(GetType(), builder);
    }

    private CacheableResource<TEntity, TResource> Declared() =>
        (CacheableResource<TEntity, TResource>)_services.Services.GetRequiredService<CacheableQueries>().For(GetType());

    private async Task<ResourceDocument<TResource>?> ReadCoreAsync(
        CacheableResource<TEntity, TResource> resource,
        Guid key,
        Guid? owner,
        Fields<TResource> fields,
        CancellationToken cancellationToken,
        Guid? named = null)
    {
        var services = _services.Services;
        var entryKey = resource.EntryKey(services.GetRequiredService<OwnedCacheRegistry>().Service, key, owner)!;
        var perCaller = resource.PerCaller.Count > 0
            && (fields.Names is null || fields.Names.Any(resource.PerCaller.Contains));

        // A read asking for nothing worked out per caller is answered from the service's own entry when it can be,
        // with the checks a client makes. Not inside a transaction: what it saved is invalidated only as it commits, so
        // the entry may be older than the transaction's own change.
        if (!perCaller && _services.Database.Database.CurrentTransaction is null && await OwnEntryAsync(resource, entryKey, key, named, services) is { } own)
        {
            return own.Resource is { } cached ? new ResourceDocument<TResource> { Data = cached, Fields = fields.Names } : null;
        }

        // Just before the query: a fill refuses what it read too long ago.
        var readStarted = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetTimestamp();
        var (entity, duplicated) = await resource.Read.FindAsync(_services.Database, key, owner, cancellationToken, ownerNamed: named is not null);
        var mapped = entity is null ? null : resource.Read.Map(entity);

        // The map's answer, never the completion's: what is worked out per caller is never written. Under a key two
        // rows hold, the first is answered and nothing is written: either could be the one a later read finds first.
        // Inside a transaction nothing is written either: under snapshot or repeatable-read isolation the row read can be
        // as old as the transaction, and the fill limit times only the statement.
        if (!duplicated && _services.Database.Database.CurrentTransaction is null
            && (entity is not null || AbsenceKnown(resource, key, named, services.GetRequiredService<IContext>())))
        {
            await CacheFill.FillAsync(services, resource, entryKey, key, owner, entity, mapped, readStarted, cancellationToken);
        }

        if (entity is null || mapped is null)
        {
            return null;
        }

        var answer = perCaller ? await CompleteAsync(entity, mapped, fields, cancellationToken) : mapped;

        return new ResourceDocument<TResource> { Data = answer, Fields = fields.Names };
    }

    /// <summary>
    /// Whether finding no row means nothing is recorded under the key, for every caller. Under a key that is the owner,
    /// row-level security narrows the read by the owner the request names, so a read naming another owner finds no row
    /// whether or not one is recorded: only a read narrowed by the key itself, or one read across owners, knows.
    /// </summary>
    private static bool AbsenceKnown(CacheableResource<TEntity, TResource> resource, Guid key, Guid? named, IContext context) =>
        !resource.KeyIsOwner
        || resource.AcrossOwners
        || named == key
        || (resource.Owner is { } axis && Named(context, axis) == key);

    /// <summary>
    /// The service's own entry, when a client would answer from it: null for a miss, otherwise the resource or, under a
    /// unique key, nothing recorded.
    /// </summary>
    private static async Task<Answered?> OwnEntryAsync(
        CacheableResource<TEntity, TResource> resource,
        string entryKey,
        Guid key,
        Guid? named,
        IServiceProvider services)
    {
        var store = services.GetRequiredService<IOwnedCacheStore>();
        var registry = services.GetRequiredService<OwnedCacheRegistry>();

        if (!store.Enabled || !registry.Fills(resource)
            || await store.ReadAsync(entryKey) is not { } entry
            || entry.ContainsKey(OwnedCacheEntry.Gone)
            || !entry.TryGetValue(OwnedCacheEntry.Format, out var format)
            || format != OwnedCacheEntry.CurrentFormat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || !OwnedByCall(resource, entry, key, services.GetRequiredService<IContext>(), named))
        {
            return null;
        }

        if (entry.ContainsKey(OwnedCacheEntry.None))
        {
            return resource.ById ? null : new Answered(null);
        }

        if (!entry.TryGetValue(OwnedCacheEntry.Data, out var data))
        {
            return null;
        }

        var json = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        try
        {
            return JsonSerializer.Deserialize<ResourceDocument<TResource>>(new JsonObject { ["data"] = JsonNode.Parse(data) }, json)
                is { Data: { } read }
                ? new Answered(read)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The entry's owner is the one the call names, as row-level security would compare it: a client's check. A row
    /// that is its own owner is the owner (a call naming another was refused already); a resource read across owners
    /// answers whatever the call names, once the entry carries a well-formed owner.
    /// </summary>
    private static bool OwnedByCall(
        ICacheableResource resource,
        IReadOnlyDictionary<string, string> entry,
        Guid key,
        IContext context,
        Guid? named)
    {
        if (resource.Ownership == ReadOwnership.None || resource.Owner is not { } kind)
        {
            return true;
        }

        if (!entry.TryGetValue(OwnedCacheEntry.Owner, out var stored) || CacheFill.OwnerOf(stored, kind) is not { } owner)
        {
            return false;
        }

        if (resource.Ownership == ReadOwnership.Self)
        {
            return owner == key;
        }

        if (resource.AcrossOwners)
        {
            return true;
        }

        // The owner the read named outright, else the one the request's context names.
        return (named ?? kind switch
        {
            Owner.Organization => context.OrganizationId,
            Owner.User => context.UserId,
            _ => context.ProviderId,
        }) == owner;
    }

    private sealed record Answered(TResource? Resource);
}
