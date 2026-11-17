namespace Bitwarden.Server.Sdk.MessageBroker.Sender;

/// <summary>
/// How the outbox's dispatcher sends what is waiting.
/// </summary>
public sealed class MessageOutboxOptions
{
    /// <summary>
    /// Whether this instance runs the dispatcher. Default true. When false, what is written to the outbox waits there
    /// until an instance that dispatches sends it.
    /// </summary>
    public bool Dispatch { get; set; } = true;

    /// <summary>
    /// How long the dispatcher waits between polls when the last batch wasn't full. Default one second.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many messages one poll claims. Default 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long an instance's claim on a message lasts before another may take it. Default thirty seconds.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest wait between attempts; the wait doubles from two seconds. Default five minutes.
    /// </summary>
    public TimeSpan MaximumBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The largest serialized message <c>SendAsync</c> accepts; a larger one is refused when it is
    /// written, so the caller's change fails with it. Default 256 KiB, Azure Service Bus Standard's
    /// limit. A message the service handles itself (<see cref="MessageOutbox.Local"/>) has no limit.
    /// </summary>
    public int MaxMessageSize { get; set; } = 256 * 1024;

    /// <summary>
    /// How often the outbox is counted for its metrics and health check, however often they ask.
    /// Default thirty seconds.
    /// </summary>
    public TimeSpan BacklogSampleInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How late the oldest due message may be before the health check reports degraded. Default ten minutes.
    /// </summary>
    public TimeSpan BacklogDegradedAge { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How late the oldest due message may be before the health check reports unhealthy. Default one hour.
    /// </summary>
    public TimeSpan BacklogUnhealthyAge { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether to wake the dispatcher as soon as a save that wrote outbox messages commits, rather
    /// than leaving them for the next poll. Off by default.
    /// </summary>
    /// <remarks>
    /// Only this instance's dispatcher is woken, and only by saves made in this instance. The poll
    /// still runs as the backstop.
    /// </remarks>
    public bool SendOnCommit { get; set; }
}
