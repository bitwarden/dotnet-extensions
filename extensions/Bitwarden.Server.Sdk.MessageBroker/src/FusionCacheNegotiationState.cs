using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// <see cref="INegotiationState"/> backed by an <see cref="IFusionCache"/>. One cache entry per
/// publisher and subscriber; per-instance TTL comes from
/// <see cref="NegotiationOptions.CacheEntryTtl"/> and expires each entry independently.
/// <para>
/// A per-topic manifest entry lists which instance identifiers exist, because the cache exposes
/// no enumeration primitive. The manifest itself does not expire — reads prune out identifiers
/// whose per-instance entry has expired, bounding its growth.
/// </para>
/// </summary>
internal sealed class FusionCacheNegotiationState(
    IFusionCache cache,
    IOptions<NegotiationOptions> options)
    : INegotiationState
{
    private readonly IFusionCache _cache = cache;
    private readonly NegotiationOptions _options = options.Value;

    public async IAsyncEnumerable<PublisherJoin> GetPublishersAsync(
        string dataTopic,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(dataTopic, ManifestType.Publishers, cancellationToken);
        var dead = new List<string>();

        try
        {
            foreach (var id in manifest)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = await _cache.TryGetAsync<PublisherJoin>(
                    PublisherKey(dataTopic, id),
                    token: cancellationToken);
                if (entry.HasValue) yield return entry.Value;
                else dead.Add(id);
            }
        }
        finally
        {
            if (dead.Count > 0)
            {
                foreach (var id in dead) manifest.Remove(id);
                await WriteManifestAsync(dataTopic, ManifestType.Publishers, manifest, CancellationToken.None);
            }
        }
    }

    public async IAsyncEnumerable<Capability> GetSubscribersAsync(
        string dataTopic,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(dataTopic, ManifestType.Subscribers, cancellationToken);
        var dead = new List<string>();

        try
        {
            foreach (var id in manifest)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = await _cache.TryGetAsync<Capability>(
                    SubscriberKey(dataTopic, id),
                    token: cancellationToken);
                if (entry.HasValue) yield return entry.Value;
                else dead.Add(id);
            }
        }
        finally
        {
            if (dead.Count > 0)
            {
                foreach (var id in dead) manifest.Remove(id);
                await WriteManifestAsync(dataTopic, ManifestType.Subscribers, manifest, CancellationToken.None);
            }
        }
    }

    public async Task UpsertPublisherAsync(
        string dataTopic,
        PublisherJoin join,
        CancellationToken cancellationToken = default)
    {
        await _cache.SetAsync(
            PublisherKey(dataTopic, join.InstanceId),
            join,
            SetTtl,
            token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, ManifestType.Publishers, cancellationToken);
        manifest.Add(join.InstanceId);
        await WriteManifestAsync(dataTopic, ManifestType.Publishers, manifest, cancellationToken);
    }

    public async Task UpsertSubscriberAsync(
        string dataTopic,
        Capability capability,
        CancellationToken cancellationToken = default)
    {
        await _cache.SetAsync(
            SubscriberKey(dataTopic, capability.InstanceId),
            capability,
            SetTtl,
            token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, ManifestType.Subscribers, cancellationToken);
        manifest.Add(capability.InstanceId);
        await WriteManifestAsync(dataTopic, ManifestType.Subscribers, manifest, cancellationToken);
    }

    public async Task RemovePublisherAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(PublisherKey(dataTopic, instanceId), token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, ManifestType.Publishers, cancellationToken);
        if (manifest.Remove(instanceId))
            await WriteManifestAsync(dataTopic, ManifestType.Publishers, manifest, cancellationToken);
    }

    public async Task RemoveSubscriberAsync(
        string dataTopic,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(SubscriberKey(dataTopic, instanceId), token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, ManifestType.Subscribers, cancellationToken);
        if (manifest.Remove(instanceId))
            await WriteManifestAsync(dataTopic, ManifestType.Subscribers, manifest, cancellationToken);
    }

    private async Task<HashSet<string>> GetManifestAsync(string dataTopic, ManifestType manifestType, CancellationToken cancellationToken)
    {
        var manifest = await _cache.GetOrDefaultAsync<HashSet<string>?>(
            ManifestKey(dataTopic, manifestType),
            defaultValue: null,
            token: cancellationToken);
        return manifest ?? new();
    }

    // Manifest has no TTL because prune-on-read bounds its growth and no expiration is
    // needed for correctness. Instance entries still expire on their own.
    private async Task WriteManifestAsync(string dataTopic, ManifestType manifestType, HashSet<string> manifest, CancellationToken cancellationToken)
        => await _cache.SetAsync(
            ManifestKey(dataTopic, manifestType),
            manifest,
            entry => entry.SetDuration(TimeSpan.MaxValue),
            token: cancellationToken);

    private void SetTtl(FusionCacheEntryOptions entry) => entry.SetDuration(_options.CacheEntryTtl);

    private static string ManifestKey(string dataTopic, ManifestType manifestType) => $"negotiation/{dataTopic}/manifest/{manifestType}";
    private static string PublisherKey(string dataTopic, string instanceId) => $"negotiation/{dataTopic}/publishers/{instanceId}";
    private static string SubscriberKey(string dataTopic, string instanceId) => $"negotiation/{dataTopic}/subscribers/{instanceId}";

    private struct ManifestType
    {
        public static ManifestType Publishers = new("publishers");
        public static ManifestType Subscribers = new("subscribers");
        private string Type { get; }
        private ManifestType(string type)
        {
            Type = type;
        }

        public override string ToString() => Type;
    }
}
