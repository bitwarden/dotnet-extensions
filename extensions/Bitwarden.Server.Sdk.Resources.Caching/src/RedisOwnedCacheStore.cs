using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Resources.Caching;

/// <summary>
/// The owned cache in Redis, written only through scripts that compare versions.
/// </summary>
/// <remarks>
/// <para>
/// A cache filled on read has a race no ordinary <c>SET</c> can close: a reader loads version 6, a writer commits 7,
/// and the reader then writes 6 back. Here a change, just before it commits, leaves a tombstone saying "6 is dead",
/// and a fill writes only a version newer than the dead one, so the slow reader's 6 is refused and the committed 7 is
/// accepted. The dead version is the one the change replaced, which the row's concurrency token vouches for: a second
/// writer holding the same version fails in the database, so two changes can't both mark 6 dead and commit different
/// sevens.
/// </para>
/// <para>
/// A tombstone lives as long as an entry would, which the host checks is longer than any fill it must refuse can take
/// to arrive (<see cref="OwnedCacheTimes"/>). An arrival's lives the commit allowance (<see cref="Invalidation.Arrival"/>).
/// </para>
/// <para>
/// Every script runs under the service's own Redis credential, so it can reach only the
/// service's own prefix (<see cref="ServiceCacheAcl"/>).
/// </para>
/// <para>
/// A tombstone works only while it is there, so the Redis must never evict one: under any
/// eviction policy a tombstone is a key with a lifetime like any other, and once it is evicted the
/// key is absent and the slow reader's old version is written after all. Before it fills
/// anything, the store reads the policy from <c>INFO memory</c> (which every service's user may
/// run, where <c>CONFIG GET</c> is refused), and fills only where Redis evicts nothing:
/// <c>maxmemory-policy noeviction</c>, Redis's own default, or no <c>maxmemory</c> at all. Elsewhere it
/// logs an error and fills nothing, so its clients ask it, and still invalidates.
/// </para>
/// </remarks>
/// <param name="connection">The service cache's connection.</param>
/// <param name="logger">Records what could not be written; never what was.</param>
internal sealed class RedisOwnedCacheStore(OwnedCacheConnection connection, ILogger<RedisOwnedCacheStore> logger) : IOwnedCacheStore
{
    /// <summary>
    /// The only eviction policy under which a tombstone stays as long as its lifetime.
    /// </summary>
    internal const string RequiredPolicy = "noeviction";

    /// <summary>
    /// Redis's memory settings, as <c>INFO memory</c> gives them.
    /// </summary>
    internal const string MemoryScript = "return redis.call('INFO', 'memory')";

    // Unknown until Redis has answered; then whether it keeps its tombstones. Read once per process.
    private int _keepsTombstones;

    /// <summary>
    /// KEYS[1] the entry; ARGV: version, lifetime in ms, owner ('' for none), data ('' for none), '1' for "nothing
    /// recorded", the entry's format. Over a tombstone, written only when newer than the version it marks dead. Over an
    /// entry, written only when newer than it; one already holding exactly this, at this version, is left as it is and
    /// the fill counts as made: a service serving its own read again would otherwise rewrite the same entry on every
    /// read, and its lifetime runs on from the first fill, the backstop it always was. A different entry at the same
    /// version is left too: two reads can't both be the row at one version, and the one written first stands until
    /// its row moves on. One at the same version in another format is the same row written as an older release wrote
    /// it, and is replaced, so a release changing the format isn't missed until every row moves.
    /// </summary>
    internal const string FillScript = """
        local held = redis.call('HMGET', KEYS[1], 'v', 'gone', 'none', 'o', 'd', 'f')
        local version = tonumber(ARGV[1])
        if held[1] then
          local current = tonumber(held[1])
          if held[2] then
            if version <= current then
              return 0
            end
          elseif current > version then
            return 0
          elseif current == version and held[6] == ARGV[6] then
            local owner = held[4] or ''
            if owner == ARGV[3]
              and ((ARGV[5] == '1' and held[3]) or (ARGV[5] ~= '1' and not held[3] and held[5] == ARGV[4])) then
              return 1
            end
            return 0
          end
        end
        redis.call('DEL', KEYS[1])
        redis.call('HSET', KEYS[1], 'v', ARGV[1], 'f', ARGV[6])
        if ARGV[3] ~= '' then
          redis.call('HSET', KEYS[1], 'o', ARGV[3])
        end
        if ARGV[5] == '1' then
          redis.call('HSET', KEYS[1], 'none', '1')
        else
          redis.call('HSET', KEYS[1], 'd', ARGV[4])
        end
        redis.call('PEXPIRE', KEYS[1], ARGV[2])
        return 1
        """;

    /// <summary>
    /// KEYS: the entries; ARGV[1] the lifetime in ms, then one dead version per key. Each key becomes a tombstone marking
    /// its version dead, unless it already is one marking that version or a newer one dead, whose lifetime is renewed,
    /// or holds an entry newer than the dead version, which a change committed since, and is kept. A tombstone already
    /// there never expires sooner for being renewed or replaced: an arrival's short one (<see cref="Invalidation.Arrival"/>)
    /// replacing a deletion's keeps the deletion's remaining life, as the deletion's tombstone still has fills to refuse.
    /// </summary>
    internal const string InvalidateScript = """
        for i, key in ipairs(KEYS) do
          local dead = tonumber(ARGV[i + 1])
          local lifetime = tonumber(ARGV[1])
          local held = redis.call('HMGET', key, 'v', 'gone')
          local current = held[1] and tonumber(held[1])
          if current and held[2] then
            local left = redis.call('PTTL', key)
            if left > lifetime then
              lifetime = left
            end
          end
          if current and held[2] and current >= dead then
            redis.call('PEXPIRE', key, lifetime)
          elseif current and not held[2] and current > dead then
            -- An entry newer than what this change makes dead: keep it.
          else
            redis.call('DEL', key)
            redis.call('HSET', key, 'v', ARGV[i + 1], 'gone', '1')
            redis.call('PEXPIRE', key, lifetime)
          end
        end
        return 1
        """;

    public bool Enabled => true;

    public async Task<IReadOnlyDictionary<string, string>?> ReadAsync(string key)
    {
        try
        {
            var database = await connection.DatabaseAsync();
            var fields = await database.HashGetAllAsync(key);

            return fields.Length == 0
                ? null
                : fields.ToDictionary(field => field.Name.ToString(), field => field.Value.ToString(), StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or ObjectDisposedException)
        {
            // An entry not read costs this read the database, nothing more.
            logger.LogWarning(exception, "Could not read the service cache entry {Key}.", key);
            return null;
        }
    }

    public async Task<bool> FillAsync(string key, long version, string? owner, string? data, TimeSpan lifetime)
    {
        try
        {
            if (!await KeepsTombstonesAsync())
            {
                return false;
            }

            var database = await connection.DatabaseAsync();
            var written = await database.ScriptEvaluateAsync(
                FillScript,
                [key],
                [
                    version,
                    (long)lifetime.TotalMilliseconds,
                    owner ?? string.Empty,
                    data ?? string.Empty,
                    data is null ? "1" : "0",
                    OwnedCacheEntry.CurrentFormat,
                ]);

            return (long)written == 1;
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException or ObjectDisposedException)
        {
            // A fill that fails costs the next reader a call to the service, nothing more.
            logger.LogWarning(exception, "Could not fill the service cache entry {Key}.", key);
            return false;
        }
    }

    public async Task InvalidateAsync(IReadOnlyCollection<Invalidation> invalidations, TimeSpan lifetime, TimeSpan arrivalLifetime)
    {
        if (invalidations.Count == 0)
        {
            return;
        }

        var database = await connection.DatabaseAsync();

        // One script per key rather than one for all: the keys of a batch can live on different
        // shards of a clustered Redis, and a script may only touch keys on one.
        foreach (var invalidation in invalidations)
        {
            await database.ScriptEvaluateAsync(
                InvalidateScript,
                [invalidation.Key],
                [(long)(invalidation.Arrival ? arrivalLifetime : lifetime).TotalMilliseconds, invalidation.Dead]);
        }
    }

    /// <summary>
    /// Whether a Redis with this memory configuration keeps every tombstone for its lifetime: it evicts nothing.
    /// </summary>
    /// <param name="maxmemory">INFO's <c>maxmemory</c>: 0 for no limit.</param>
    /// <param name="policy">INFO's <c>maxmemory_policy</c>.</param>
    internal static bool KeepsTombstones(string? maxmemory, string? policy) =>
        maxmemory == "0" || string.Equals(policy, RequiredPolicy, StringComparison.Ordinal);

    /// <summary>
    /// Whether Redis keeps its tombstones, read from <c>INFO memory</c> the first time Redis answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through a script, because the client library allows <c>INFO</c> itself only on a connection in admin mode,
    /// which would allow every other administrative command too. Every service's user may run <c>INFO</c>.
    /// </para>
    /// <para>
    /// Not kept until Redis has answered, so a Redis that is away when the service starts is asked again. A clustered
    /// Redis is configured alike on every shard, so one answers for all.
    /// </para>
    /// </remarks>
    private async Task<bool> KeepsTombstonesAsync()
    {
        if (Volatile.Read(ref _keepsTombstones) is var known and not 0)
        {
            return known > 0;
        }

        var database = await connection.DatabaseAsync();
        var memory = ((string?)await database.ScriptEvaluateAsync(MemoryScript))?
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(':', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal)
            ?? [];
        memory.TryGetValue("maxmemory", out var maxmemory);
        memory.TryGetValue("maxmemory_policy", out var policy);

        if (KeepsTombstones(maxmemory, policy))
        {
            Volatile.Write(ref _keepsTombstones, 1);
            return true;
        }

        if (Interlocked.Exchange(ref _keepsTombstones, -1) == 0)
        {
            logger.LogError(
                "The service cache's Redis may evict keys (maxmemory-policy {Policy}), and an evicted tombstone lets a "
                + "read older than the change that made it put the old row back. Nothing is filled until it is "
                + "configured with maxmemory-policy {Required}; every read is answered by the service.",
                policy,
                RequiredPolicy);
        }

        return false;
    }
}
