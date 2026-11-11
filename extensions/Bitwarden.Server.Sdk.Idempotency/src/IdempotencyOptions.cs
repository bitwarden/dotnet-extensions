namespace Bitwarden.Server.Sdk.Idempotency;

/// <summary>
/// How long a service keeps what it needs to answer a repeated write: the <c>Idempotency</c> section.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    /// The configuration section.
    /// </summary>
    public const string Section = "Idempotency";

    /// <summary>
    /// The configuration key of the Redis records are kept in: <c>Idempotency:Redis:Configuration</c>.
    /// </summary>
    /// <remarks>
    /// A StackExchange.Redis connection string. Unset, or without a data protection key ring every instance reads,
    /// each instance keeps its own records in memory.
    /// </remarks>
    public const string RedisConfigurationKey = "Idempotency:Redis:Configuration";

    /// <summary>
    /// How long a completed write's answer is kept for its key: a repeat after that is done again. 24 hours by
    /// default, as payment processors keep theirs.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a write in progress holds its key: a repeat meanwhile is refused <c>409</c>; one after, should the
    /// instance running it have stopped, is done again.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The largest request body fingerprinted, and the largest answer kept, in bytes. A write past either is run
    /// without a key, as it would be without one.
    /// </summary>
    public int MaximumBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Whether keys are honoured at all. On by default.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
