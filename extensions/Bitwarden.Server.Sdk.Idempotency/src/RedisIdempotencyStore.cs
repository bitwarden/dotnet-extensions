using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// Keys held in Redis (<c>Idempotency:Redis:Configuration</c>), under the service's own prefix, so every
/// instance of the service sees one record per key: a repeat that reaches another instance is still answered once.
/// </summary>
/// <remarks>
/// A claim is <c>SET NX</c>, atomic, so two instances receiving one key at once run it once. While Redis is away the
/// write runs without a key, as it would without the header: an outage of Redis never stops a write, as it never
/// stops a rate-limited one.
/// </remarks>
internal sealed class RedisIdempotencyStore(
    Func<Task<IConnectionMultiplexer>> connect,
    ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore, IDisposable
{
    // Deleted only while it still holds this write's own pending record: never another's, nor an answer.
    private const string ReleaseIfHeld = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly Lazy<Task<IConnectionMultiplexer>> _redis = new(connect);

    /// <summary>
    /// Connects to the configured Redis on first use, without failing when it is away: it reconnects on
    /// its own. A connection string that doesn't parse fails here, at startup, not on a request.
    /// </summary>
    /// <param name="connectionString">The Redis configuration (<c>Idempotency:Redis:Configuration</c>).</param>
    public static Func<Task<IConnectionMultiplexer>> Connect(string connectionString)
    {
        var configuration = ConfigurationOptions.Parse(connectionString);
        configuration.AbortOnConnectFail = false;

        return async () => await ConnectionMultiplexer.ConnectAsync(configuration);
    }

    public async ValueTask<Claim> ClaimAsync(string key, string pending, TimeSpan lease)
    {
        try
        {
            var database = await DatabaseAsync();
            if (database is null)
            {
                return Claim.Unavailable;
            }

            // Twice at most: a record that expires between the SET and the GET is gone, and the second SET takes it.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (await database.StringSetAsync(key, pending, lease, When.NotExists))
                {
                    return Claim.Held;
                }

                if (await database.StringGetAsync(key) is { HasValue: true } existing)
                {
                    return Claim.Found(existing.ToString());
                }
            }

            return Claim.Unavailable;
        }
        catch (Exception failure) when (failure is RedisException or TimeoutException)
        {
            logger.LogWarning(failure, "The idempotency store's Redis is unavailable; a write runs without its idempotency key.");
            return Claim.Unavailable;
        }
    }

    public async ValueTask CompleteAsync(string key, string answer, TimeSpan retention)
    {
        try
        {
            if (await DatabaseAsync() is { } database)
            {
                await database.StringSetAsync(key, answer, retention);
            }
        }
        catch (Exception failure) when (failure is RedisException or TimeoutException)
        {
            logger.LogWarning(failure, "The idempotency store's Redis is unavailable; a write's answer is not kept for its key.");
        }
    }

    public async ValueTask ReleaseAsync(string key, string pending)
    {
        try
        {
            if (await DatabaseAsync() is { } database)
            {
                await database.ScriptEvaluateAsync(ReleaseIfHeld, [new RedisKey(key)], [new RedisValue(pending)]);
            }
        }
        catch (Exception failure) when (failure is RedisException or TimeoutException)
        {
            // Held until its lease ends: a repeat meanwhile is refused 409, then run.
            logger.LogWarning(failure, "The idempotency store's Redis is unavailable; a failed write's key is held until its lease ends.");
        }
    }

    public void Dispose()
    {
        if (_redis.IsValueCreated && _redis.Value.IsCompletedSuccessfully)
        {
            _redis.Value.Result.Dispose();
        }
    }

    private async Task<IDatabase?> DatabaseAsync()
    {
        var redis = await _redis.Value;
        return redis.IsConnected ? redis.GetDatabase() : null;
    }
}
