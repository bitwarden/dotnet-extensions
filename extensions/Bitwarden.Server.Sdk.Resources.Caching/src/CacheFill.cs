using System.Text.Json;
using System.Text.Json.Nodes;
using Bitwarden.Server.Sdk.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// Writes what the platform's read returned into the cache: write on read.
/// </summary>
internal static class CacheFill
{
    /// <summary>
    /// The version of "nothing recorded": older than any row.
    /// </summary>
    public const long NothingRecorded = -1;

    /// <summary>
    /// Fills the entry with what the read returned, unless the read began longer ago than the fill limit
    /// (<see cref="OwnedCacheTimes.FillLimit"/>).
    /// </summary>
    /// <param name="services">The request's services.</param>
    /// <param name="resource">The declaration.</param>
    /// <param name="entryKey">The entry's key.</param>
    /// <param name="key">The identifier or key read.</param>
    /// <param name="owner">The owner read within, for a key within its owner.</param>
    /// <param name="entity">The row, or null when there is none.</param>
    /// <param name="mapped">The declaration's map of the row.</param>
    /// <param name="readStarted">When the database read began, on the request's <see cref="TimeProvider"/>.</param>
    /// <param name="cancellationToken">Cancels the fill.</param>
    public static async ValueTask FillAsync<TEntity, TResource>(
        IServiceProvider services,
        CacheableResource<TEntity, TResource> resource,
        string entryKey,
        Guid key,
        Guid? owner,
        TEntity? entity,
        TResource? mapped,
        long readStarted,
        CancellationToken cancellationToken)
        where TEntity : class
        where TResource : Resource
    {
        var store = services.GetRequiredService<IOwnedCacheStore>();
        var registry = services.GetRequiredService<OwnedCacheRegistry>();

        if (!store.Enabled || !registry.Fills(resource))
        {
            return;
        }

        if (TooOld(services, readStarted, entryKey))
        {
            return;
        }

        if (entity is null || mapped is null)
        {
            // Nothing recorded under a unique key is an answer too, and asked for as often as a
            // recorded one: an unentitled organization is asked about just as often as an
            // entitled one. Below every real version, so the first write's tombstone refuses it.
            // Only where the key is the owner, or names a row within its owner, so the entry's owner is known without a
            // row.
            if (!resource.ById && (resource.KeyIsOwner || (resource.WithinOwner && owner is not null)))
            {
                await store.FillAsync(
                    entryKey, NothingRecorded, Owner(resource, key, resource.KeyIsOwner ? key : owner), null, resource.Lifetime);
            }

            return;
        }

        var json = services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        // Shaped exactly as the service serves it to a caller holding the read scope: the same converter, the same
        // naming, every attribute but those worked out per caller.
        var document = JsonSerializer.SerializeToNode(
            new ResourceDocument<TResource> { Data = mapped, Fields = resource.Fields.ToHashSet(StringComparer.Ordinal) },
            json)!;
        var data = document["data"]!.ToJsonString();

        await store.FillAsync(
            entryKey,
            resource.VersionOf(entity),
            Owner(resource, key, resource.Read.OwnerOf(entity, key)),
            data,
            resource.Lifetime);
    }

    /// <summary>
    /// Whether what was read began longer ago than the fill limit: then it may predate a change whose tombstone is
    /// older than any tombstone is kept, and is not written. Measured on the reader's own clock, from just before its
    /// read to now.
    /// </summary>
    internal static bool TooOld(IServiceProvider services, long readStarted, string key)
    {
        var limit = services.GetRequiredService<OwnedCacheTimes>().FillLimit;
        var elapsed = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetElapsedTime(readStarted);

        if (elapsed <= limit)
        {
            return false;
        }

        services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CacheFill)).LogDebug(
            "Did not fill the service cache entry {Key}: its read began {Elapsed} ago, longer than the fill limit {Limit}.",
            key,
            elapsed,
            limit);
        return true;
    }

    private static string? Owner(ICacheableResource resource, Guid key, Guid? owner)
    {
        var value = resource.Ownership switch
        {
            ReadOwnership.Self => key,
            ReadOwnership.Rows => owner,
            _ => null,
        };

        return value is { } id && resource.Owner is { } kind
            ? new JsonObject { [Axis(kind)] = id.ToString("D") }.ToJsonString()
            : null;
    }

    /// <summary>
    /// The owner an entry's owner field names on one axis; null when unreadable or naming none.
    /// </summary>
    public static Guid? OwnerOf(string stored, Owner owner)
    {
        try
        {
            return JsonNode.Parse(stored) is JsonObject owners
                && owners[Axis(owner)] is JsonValue value
                && value.TryGetValue<string>(out var named)
                && Guid.TryParse(named, out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Axis(Owner owner) => owner switch
    {
        Data.Owner.Organization => "organization",
        Data.Owner.User => "user",
        _ => "provider",
    };
}
