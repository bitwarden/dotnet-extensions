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
    /// How long a completed write's answer is kept for its key: a repeat after that is done again. 10 minutes by
    /// default.
    /// </summary>
    /// <remarks>
    /// Long enough for a client's retries, which end within the call's deadline, and for an operation run again after
    /// a failure: a write held by <see cref="Lease"/>, then a dispatcher's backoff of at most five minutes. Records are
    /// held in the store for this long, so the store holds about the answers of this many minutes of keyed writes.
    /// </remarks>
    public TimeSpan Retention { get; set; } = TimeSpan.FromMinutes(10);

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
