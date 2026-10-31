namespace Bitwarden.Server.Sdk.Service.Http;

/// <summary>
/// Shared counts, read from the <c>globalSettings:distributedIpRateLimiting</c> section: with a Redis
/// connection string, every instance of a service counts in one place.
/// </summary>
public sealed class DistributedIpRateLimitingOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string SectionName = "globalSettings:distributedIpRateLimiting";

    /// <summary>
    /// The Redis to count in. Without one, each process counts on its own.
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Whether counts are shared at all, given a connection string.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many Redis timeouts or connection failures within <see cref="SlidingWindowSeconds"/>
    /// set Redis aside until the window closes. Defaults to 10.
    /// </summary>
    public int MaxRedisTimeoutsThreshold { get; set; } = 10;

    /// <summary>
    /// How long, from the first failure, failures are counted. Defaults to 120 seconds.
    /// </summary>
    public int SlidingWindowSeconds { get; set; } = 120;
}
