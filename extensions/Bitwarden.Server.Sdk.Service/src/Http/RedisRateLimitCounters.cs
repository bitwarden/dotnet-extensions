using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Counts in Redis: one key per window, incremented and given the period's expiry atomically, so
/// every instance of the service draws on one allowance.
/// </summary>
/// <remarks>
/// <para>
/// It skips limiting (allows the request) while Redis is disconnected, after
/// <see cref="DistributedIpRateLimitingOptions.MaxRedisTimeoutsThreshold"/> failures within
/// <see cref="DistributedIpRateLimitingOptions.SlidingWindowSeconds"/>, and on a timeout or connection
/// failure, so a Redis outage never stops requests.
/// </para>
/// <para>
/// Keys are <c>{service}:ip-rate-limit:{SHA-256 of the bucket}</c>, under the service's name, so two
/// services carrying a rule for the same route (a proxy service in front of Identity, say) don't each
/// count a request the other has counted.
/// </para>
/// </remarks>
internal sealed class RedisRateLimitCounters : IRateLimitCounters, IDisposable
{
    // INCR, and the window's expiry when the key is new (or lost its expiry), in one step.
    private const string Increment = """
        local count = redis.call('INCR', KEYS[1])
        if redis.call('PTTL', KEYS[1]) < 0 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    private readonly Lazy<Task<IConnectionMultiplexer>> _redis;
    private readonly string _prefix;
    private readonly DistributedIpRateLimitingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RedisRateLimitCounters> _logger;
    private readonly Lock _failuresLock = new();
    private int _failures;
    private DateTimeOffset _failuresExpire;

    public RedisRateLimitCounters(
        Func<Task<IConnectionMultiplexer>> connect,
        string serviceName,
        DistributedIpRateLimitingOptions options,
        TimeProvider time,
        ILogger<RedisRateLimitCounters> logger)
    {
        _redis = new Lazy<Task<IConnectionMultiplexer>>(connect);
        _prefix = $"{serviceName}:ip-rate-limit:";
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Connects to the configured Redis on first use, without failing when it is away: it reconnects on
    /// its own. A connection string that doesn't parse fails here, at startup, not on a request.
    /// </summary>
    public static Func<Task<IConnectionMultiplexer>> Connect(string connectionString)
    {
        var configuration = ConfigurationOptions.Parse(connectionString);
        configuration.AbortOnConnectFail = false;

        return async () => await ConnectionMultiplexer.ConnectAsync(configuration);
    }

    public async ValueTask<bool> TryTakeAsync(string bucket, int limit, TimeSpan period, CancellationToken cancellationToken = default)
    {
        if (SetAside())
        {
            _logger.LogDebug("Redis timeout threshold has been exceeded; skipping IP rate limiting.");
            return true;
        }

        try
        {
            var redis = await _redis.Value;

            if (!redis.IsConnected)
            {
                _logger.LogDebug("Redis is not connected; skipping IP rate limiting.");
                return true;
            }

            var count = (long)await redis.GetDatabase().ScriptEvaluateAsync(
                Increment,
                [new RedisKey(_prefix + Hash(bucket))],
                [(long)Math.Ceiling(period.TotalMilliseconds)]);

            return count <= Math.Max(limit, 1);
        }
        catch (Exception failure) when (failure is RedisException or TimeoutException)
        {
            _logger.LogWarning(failure, "Redis appears down; skipping IP rate limiting.");
            Failed();

            return true;
        }
    }

    public void Dispose()
    {
        if (_redis.IsValueCreated && _redis.Value.IsCompletedSuccessfully)
        {
            _redis.Value.Result.Dispose();
        }
    }

    /// <summary>
    /// Whether enough failures within the sliding window set Redis aside.
    /// </summary>
    private bool SetAside()
    {
        lock (_failuresLock)
        {
            if (_failures > 0 && _time.GetUtcNow() >= _failuresExpire)
            {
                _failures = 0;
            }

            return _failures >= _options.MaxRedisTimeoutsThreshold;
        }
    }

    /// <summary>
    /// Counts a failure; the first opens the sliding window.
    /// </summary>
    private void Failed()
    {
        lock (_failuresLock)
        {
            if (_failures == 0)
            {
                _failuresExpire = _time.GetUtcNow().AddSeconds(_options.SlidingWindowSeconds);
            }

            _failures++;
        }
    }

    private static string Hash(string bucket) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(bucket)));
}
