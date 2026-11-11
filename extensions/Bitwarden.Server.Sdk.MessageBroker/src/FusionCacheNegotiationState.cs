using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace Bitwarden.Server.Sdk.MessageBroker;

/// <summary>
/// <see cref="INegotiationState"/> backed by an <see cref="IFusionCache"/>. One cache entry per
/// <c>(dataTopic, familyWireName, instanceId)</c>; per-instance TTL comes from
/// <see cref="NegotiationOptions.CacheEntryTtl"/> and expires each entry independently.
/// <para>
/// A per-<c>(dataTopic, familyWireName)</c> manifest entry lists which instance identifiers
/// exist in that bucket, because the cache exposes no enumeration primitive. The manifest
/// itself does not expire — reads prune out identifiers whose per-instance entry has expired,
/// bounding its growth.
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
        string familyWireName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(dataTopic, familyWireName, ManifestType.Publishers, cancellationToken);
        var dead = new List<string>();

        try
        {
            foreach (var id in manifest)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = await _cache.TryGetAsync<PublisherJoin>(
                    PublisherKey(dataTopic, familyWireName, id),
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
                await WriteManifestAsync(dataTopic, familyWireName, ManifestType.Publishers, manifest, CancellationToken.None);
            }
        }
    }

    public async IAsyncEnumerable<Capability> GetSubscribersAsync(
        string dataTopic,
        string familyWireName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(dataTopic, familyWireName, ManifestType.Subscribers, cancellationToken);
        var dead = new List<string>();

        try
        {
            foreach (var id in manifest)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = await _cache.TryGetAsync<Capability>(
                    SubscriberKey(dataTopic, familyWireName, id),
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
                await WriteManifestAsync(dataTopic, familyWireName, ManifestType.Subscribers, manifest, CancellationToken.None);
            }
        }
    }

    public async Task<PublisherJoin?> TryGetPublisherAsync(
        string dataTopic,
        string familyWireName,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _cache.TryGetAsync<PublisherJoin>(
            PublisherKey(dataTopic, familyWireName, instanceId),
            token: cancellationToken);
        return entry.HasValue ? entry.Value : null;
    }

    public async Task<Capability?> TryGetSubscriberAsync(
        string dataTopic,
        string familyWireName,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        var entry = await _cache.TryGetAsync<Capability>(
            SubscriberKey(dataTopic, familyWireName, instanceId),
            token: cancellationToken);
        return entry.HasValue ? entry.Value : null;
    }

    public async Task UpsertPublisherAsync(
        string dataTopic,
        PublisherJoin join,
        CancellationToken cancellationToken = default)
    {
        await _cache.SetAsync(
            PublisherKey(dataTopic, join.FamilyWireName, join.InstanceId),
            join,
            SetTtl,
            token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, join.FamilyWireName, ManifestType.Publishers, cancellationToken);
        manifest.Add(join.InstanceId);
        await WriteManifestAsync(dataTopic, join.FamilyWireName, ManifestType.Publishers, manifest, cancellationToken);
    }

    public async Task UpsertSubscriberAsync(
        string dataTopic,
        Capability capability,
        CancellationToken cancellationToken = default)
    {
        await _cache.SetAsync(
            SubscriberKey(dataTopic, capability.FamilyWireName, capability.InstanceId),
            capability,
            SetTtl,
            token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, capability.FamilyWireName, ManifestType.Subscribers, cancellationToken);
        manifest.Add(capability.InstanceId);
        await WriteManifestAsync(dataTopic, capability.FamilyWireName, ManifestType.Subscribers, manifest, cancellationToken);
    }

    public async Task RemovePublisherAsync(
        string dataTopic,
        string familyWireName,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(PublisherKey(dataTopic, familyWireName, instanceId), token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, familyWireName, ManifestType.Publishers, cancellationToken);
        if (manifest.Remove(instanceId))
            await WriteManifestAsync(dataTopic, familyWireName, ManifestType.Publishers, manifest, cancellationToken);
    }

    public async Task RemoveSubscriberAsync(
        string dataTopic,
        string familyWireName,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(SubscriberKey(dataTopic, familyWireName, instanceId), token: cancellationToken);

        var manifest = await GetManifestAsync(dataTopic, familyWireName, ManifestType.Subscribers, cancellationToken);
        if (manifest.Remove(instanceId))
            await WriteManifestAsync(dataTopic, familyWireName, ManifestType.Subscribers, manifest, cancellationToken);
    }

    private async Task<HashSet<string>> GetManifestAsync(string dataTopic, string familyWireName, ManifestType manifestType, CancellationToken cancellationToken)
    {
        var manifest = await _cache.GetOrDefaultAsync<HashSet<string>?>(
            ManifestKey(dataTopic, familyWireName, manifestType),
            defaultValue: null,
            token: cancellationToken);
        return manifest ?? new();
    }

    // Manifest has no TTL because prune-on-read bounds its growth and no expiration is
    // needed for correctness. Instance entries still expire on their own.
    private async Task WriteManifestAsync(string dataTopic, string familyWireName, ManifestType manifestType, HashSet<string> manifest, CancellationToken cancellationToken)
        => await _cache.SetAsync(
            ManifestKey(dataTopic, familyWireName, manifestType),
            manifest,
            entry => entry.SetDuration(TimeSpan.MaxValue),
            token: cancellationToken);

    private void SetTtl(FusionCacheEntryOptions entry) => entry.SetDuration(_options.CacheEntryTtl);

    private static string ManifestKey(string dataTopic, string familyWireName, ManifestType manifestType) => $"negotiation/{dataTopic}/{familyWireName}/manifest/{manifestType}";
    private static string PublisherKey(string dataTopic, string familyWireName, string instanceId) => $"negotiation/{dataTopic}/{familyWireName}/publishers/{instanceId}";
    private static string SubscriberKey(string dataTopic, string familyWireName, string instanceId) => $"negotiation/{dataTopic}/{familyWireName}/subscribers/{instanceId}";

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
